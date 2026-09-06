using System.Runtime.Versioning;
using System.Security;
using System.Text.Json;
using AccountRotation.Core;
using AccountRotation.Core.Switching;
using Microsoft.Win32;

namespace AccountRotation.App;

/// <summary>
/// Reads the device's managed login policy the way Claude Code ranks its
/// local admin sources (docs, "Deploy managed settings"): the HKLM policy
/// value, then the managed settings file, then the user-writable HKCU value
/// when nothing above it exists. <c>forceLoginOrgUUID</c> is read from the
/// highest-ranked present source only, exactly as the CLI reads it. A source
/// that is present but cannot be read (a value of the wrong registry type, an
/// access denial, malformed JSON) is reported as unreadable and never skipped:
/// skipping it would let a lower-ranked, user-writable source stand in for the
/// enterprise pin. Remote (server-managed) settings are not visible locally
/// and are not consulted.
/// </summary>
internal sealed class ManagedLoginPolicyReader
{
    private const string ForceLoginOrgUuidKey = "forceLoginOrgUUID";
    private const string PolicyRegistryPath = @"SOFTWARE\Policies\ClaudeCode";
    private const string PolicyRegistryValue = "Settings";
    private const string MachineRegistrySource = @"HKLM\SOFTWARE\Policies\ClaudeCode\Settings";
    private const string UserRegistrySource = @"HKCU\SOFTWARE\Policies\ClaudeCode\Settings";

    private readonly string _managedSettingsPath;
    private readonly Func<Result<string?, string>> _machinePolicyJson;
    private readonly Func<Result<string?, string>> _userPolicyJson;

    /// <summary>
    /// Each source delegate yields the source's JSON text, null when the source is
    /// absent, or a failure naming why a present source could not be read.
    /// </summary>
    public ManagedLoginPolicyReader(string managedSettingsPath, Func<Result<string?, string>> machinePolicyJson, Func<Result<string?, string>> userPolicyJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedSettingsPath);
        ArgumentNullException.ThrowIfNull(machinePolicyJson);
        ArgumentNullException.ThrowIfNull(userPolicyJson);
        _managedSettingsPath = managedSettingsPath;
        _machinePolicyJson = machinePolicyJson;
        _userPolicyJson = userPolicyJson;
    }

    /// <summary>The two-state form for sources that are either present or absent, never unreadable.</summary>
    public ManagedLoginPolicyReader(string managedSettingsPath, Func<string?> machinePolicyJson, Func<string?> userPolicyJson)
        : this(
            managedSettingsPath,
            () => Result<string?, string>.Success((machinePolicyJson ?? throw new ArgumentNullException(nameof(machinePolicyJson)))()),
            () => Result<string?, string>.Success((userPolicyJson ?? throw new ArgumentNullException(nameof(userPolicyJson)))()))
    {
    }

    public static ManagedLoginPolicyReader ForCurrentMachine() => new(
        DefaultManagedSettingsPath(),
        static () => OperatingSystem.IsWindows() ? ReadRegistry(RegistryHive.LocalMachine) : Result<string?, string>.Success(null),
        static () => OperatingSystem.IsWindows() ? ReadRegistry(RegistryHive.CurrentUser) : Result<string?, string>.Success(null));

    public async Task<ManagedLoginPolicy> ReadAsync(CancellationToken cancellationToken)
    {
        Result<string?, string> machine = _machinePolicyJson();
        if (machine.IsFailure)
        {
            return Unreadable(MachineRegistrySource, machine.Error);
        }

        if (machine.Value is string machinePolicy)
        {
            return Evaluate(machinePolicy, MachineRegistrySource);
        }

        if (File.Exists(_managedSettingsPath))
        {
            string text;
            try
            {
                text = await File.ReadAllTextAsync(_managedSettingsPath, cancellationToken);
            }
            catch (IOException exception)
            {
                return Unreadable(_managedSettingsPath, exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                return Unreadable(_managedSettingsPath, exception.Message);
            }

            return Evaluate(text, _managedSettingsPath);
        }

        Result<string?, string> user = _userPolicyJson();
        if (user.IsFailure)
        {
            return Unreadable(UserRegistrySource, user.Error);
        }

        if (user.Value is string userPolicy)
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
                return Unreadable(source, "not a JSON object");
            }

            string? organization = document.RootElement.TryGetProperty(ForceLoginOrgUuidKey, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
            return new ManagedLoginPolicy(string.IsNullOrWhiteSpace(organization) ? null : organization, source);
        }
        catch (JsonException exception)
        {
            return Unreadable(source, exception.Message);
        }
    }

    private static ManagedLoginPolicy Unreadable(string source, string reason) =>
        new(null, source + " (unreadable: " + reason + ")", Unreadable: true);

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

    /// <summary>
    /// Null when the key or value is absent; a failure when the value exists but is
    /// not a string (the policy is a REG_SZ of JSON) or the key cannot be opened.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static Result<string?, string> ReadRegistry(RegistryHive hive)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? key = baseKey.OpenSubKey(PolicyRegistryPath);
            object? value = key?.GetValue(PolicyRegistryValue);
            return value switch
            {
                null => Result<string?, string>.Success(null),
                string text => Result<string?, string>.Success(text),
                _ => Result<string?, string>.Failure("the value is a " + key!.GetValueKind(PolicyRegistryValue) + ", not the REG_SZ the policy is read from"),
            };
        }
        catch (SecurityException exception)
        {
            return Result<string?, string>.Failure("access denied: " + exception.Message);
        }
        catch (IOException exception)
        {
            return Result<string?, string>.Failure(exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Result<string?, string>.Failure("access denied: " + exception.Message);
        }
    }
}
