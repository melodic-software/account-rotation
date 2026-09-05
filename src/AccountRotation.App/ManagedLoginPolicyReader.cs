using System.Runtime.Versioning;
using System.Security;
using System.Text.Json;
using AccountRotation.Core.Switching;
using Microsoft.Win32;

namespace AccountRotation.App;

/// <summary>
/// Reads the device's managed login policy the way Claude Code ranks its
/// local admin sources (docs, "Deploy managed settings"): the HKLM policy
/// value, then the managed settings file, then the user-writable HKCU value
/// when nothing above it exists. <c>forceLoginOrgUUID</c> is read from the
/// highest-ranked present source only, exactly as the CLI reads it. Remote
/// (server-managed) settings are not visible locally and are not consulted.
/// </summary>
internal sealed class ManagedLoginPolicyReader
{
    private const string ForceLoginOrgUuidKey = "forceLoginOrgUUID";
    private const string PolicyRegistryPath = @"SOFTWARE\Policies\ClaudeCode";
    private const string PolicyRegistryValue = "Settings";
    private const string MachineRegistrySource = @"HKLM\SOFTWARE\Policies\ClaudeCode\Settings";
    private const string UserRegistrySource = @"HKCU\SOFTWARE\Policies\ClaudeCode\Settings";

    private readonly string _managedSettingsPath;
    private readonly Func<string?> _machinePolicyJson;
    private readonly Func<string?> _userPolicyJson;

    public ManagedLoginPolicyReader(string managedSettingsPath, Func<string?> machinePolicyJson, Func<string?> userPolicyJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedSettingsPath);
        ArgumentNullException.ThrowIfNull(machinePolicyJson);
        ArgumentNullException.ThrowIfNull(userPolicyJson);
        _managedSettingsPath = managedSettingsPath;
        _machinePolicyJson = machinePolicyJson;
        _userPolicyJson = userPolicyJson;
    }

    public static ManagedLoginPolicyReader ForCurrentMachine() => new(
        DefaultManagedSettingsPath(),
        static () => OperatingSystem.IsWindows() ? ReadRegistry(RegistryHive.LocalMachine) : null,
        static () => OperatingSystem.IsWindows() ? ReadRegistry(RegistryHive.CurrentUser) : null);

    public async Task<ManagedLoginPolicy> ReadAsync(CancellationToken cancellationToken)
    {
        if (_machinePolicyJson() is string machinePolicy)
        {
            return Evaluate(machinePolicy, MachineRegistrySource);
        }

        if (File.Exists(_managedSettingsPath))
        {
            return Evaluate(await File.ReadAllTextAsync(_managedSettingsPath, cancellationToken), _managedSettingsPath);
        }

        if (_userPolicyJson() is string userPolicy)
        {
            return Evaluate(userPolicy, UserRegistrySource);
        }

        return ManagedLoginPolicy.None;
    }

    private static ManagedLoginPolicy Evaluate(string json, string source)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new ManagedLoginPolicy(null, source + " (unreadable: not a JSON object)");
            }

            string? organization = document.RootElement.TryGetProperty(ForceLoginOrgUuidKey, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
            return new ManagedLoginPolicy(string.IsNullOrWhiteSpace(organization) ? null : organization, source);
        }
        catch (JsonException exception)
        {
            return new ManagedLoginPolicy(null, source + " (unreadable: " + exception.Message + ")");
        }
    }

    private static string DefaultManagedSettingsPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClaudeCode", "managed-settings.json");
        }

        return OperatingSystem.IsMacOS()
            ? "/Library/Application Support/ClaudeCode/managed-settings.json"
            : "/etc/claude-code/managed-settings.json";
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadRegistry(RegistryHive hive)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? key = baseKey.OpenSubKey(PolicyRegistryPath);
            return key?.GetValue(PolicyRegistryValue) as string;
        }
        catch (SecurityException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
