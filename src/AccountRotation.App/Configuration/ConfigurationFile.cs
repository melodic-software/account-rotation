using System.Text.Json;
using System.Text.Json.Nodes;
using AccountRotation.App.Adapters.FileSystem;
using AccountRotation.Core;
using AccountRotation.Core.Configuration;

namespace AccountRotation.App.Configuration;

/// <summary>
/// <c>config.json</c> under app data. A missing file is created on first run
/// with the defaults resolved for this user, so what the user edits is what
/// the tool runs with; an existing file overrides the defaults key by key, and
/// a null or absent key keeps the default.
/// </summary>
internal static class ConfigurationFile
{
    public static async Task<Result<AccountRotationConfiguration, string>> LoadOrCreateAsync(
        string path,
        AccountRotationConfiguration defaults,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(defaults);
        string fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await AtomicJsonFile.WriteAsync(fullPath, ToJson(defaults), cancellationToken);
            return Result<AccountRotationConfiguration, string>.Success(defaults);
        }

        JsonObject raw;
        try
        {
            var node = JsonNode.Parse(await SharedFileReader.ReadAllBytesAsync(fullPath, cancellationToken), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (node is not JsonObject parsed)
            {
                return Result<AccountRotationConfiguration, string>.Failure("the configuration file " + fullPath + " is not a JSON object");
            }

            raw = parsed;
        }
        catch (JsonException exception)
        {
            return Result<AccountRotationConfiguration, string>.Failure("the configuration file " + fullPath + " could not be parsed: " + exception.Message);
        }

        return Result<AccountRotationConfiguration, string>.Success(Merge(defaults, raw));
    }

    private static JsonObject ToJson(AccountRotationConfiguration configuration) => new()
    {
        ["liveConfigDirectory"] = configuration.LiveConfigDirectory,
        ["stateFilePath"] = configuration.StateFilePath,
        ["profilesRoot"] = configuration.ProfilesRoot,
        ["appDataDirectory"] = configuration.AppDataDirectory,
        ["listenPort"] = configuration.ListenPort,
        ["refreshLockWaitSeconds"] = configuration.RefreshLockWaitBound.TotalSeconds,
        ["claudeExecutable"] = configuration.ClaudeExecutable,
        ["patchStateFile"] = configuration.PatchStateFile,
        ["userAgentProductToken"] = configuration.UserAgentProductToken,
    };

    private static AccountRotationConfiguration Merge(AccountRotationConfiguration defaults, JsonObject raw) => new(
        Text(raw, "liveConfigDirectory") ?? defaults.LiveConfigDirectory,
        Text(raw, "stateFilePath") ?? defaults.StateFilePath,
        Text(raw, "profilesRoot") ?? defaults.ProfilesRoot,
        Text(raw, "appDataDirectory") ?? defaults.AppDataDirectory,
        Number(raw, "listenPort") is double port ? (int)port : defaults.ListenPort,
        Number(raw, "refreshLockWaitSeconds") is double seconds ? TimeSpan.FromSeconds(seconds) : defaults.RefreshLockWaitBound,
        Text(raw, "claudeExecutable") ?? defaults.ClaudeExecutable,
        Flag(raw, "patchStateFile") ?? defaults.PatchStateFile,
        Text(raw, "userAgentProductToken") ?? defaults.UserAgentProductToken);

    private static string? Text(JsonObject raw, string key) =>
        raw[key] is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static double? Number(JsonObject raw, string key) =>
        raw[key] is JsonValue value && value.TryGetValue(out double number) ? number : null;

    private static bool? Flag(JsonObject raw, string key) =>
        raw[key] is JsonValue value && value.TryGetValue(out bool flag) ? flag : null;
}
