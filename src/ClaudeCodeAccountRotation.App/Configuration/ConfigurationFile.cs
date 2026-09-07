using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeCodeAccountRotation.App.Adapters.FileSystem;
using ClaudeCodeAccountRotation.Core;
using ClaudeCodeAccountRotation.Core.Configuration;

namespace ClaudeCodeAccountRotation.App.Configuration;

/// <summary>
/// <c>config.json</c> under app data. A missing file is created on first run
/// with the defaults resolved for this user, so what the user edits is what
/// the tool runs with; an existing file overrides the defaults key by key, and
/// a null or absent key keeps the default.
/// </summary>
internal static class ConfigurationFile
{
    public static async Task<Result<ClaudeCodeAccountRotationConfiguration, string>> LoadOrCreateAsync(
        string path,
        ClaudeCodeAccountRotationConfiguration defaults,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(defaults);
        string fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await AtomicJsonFile.WriteAsync(fullPath, ToJson(defaults), cancellationToken);
            return Result<ClaudeCodeAccountRotationConfiguration, string>.Success(defaults);
        }

        JsonObject raw;
        try
        {
            var node = JsonNode.Parse(await SharedFileReader.ReadAllBytesAsync(fullPath, cancellationToken), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (node is not JsonObject parsed)
            {
                return Result<ClaudeCodeAccountRotationConfiguration, string>.Failure("the configuration file " + fullPath + " is not a JSON object");
            }

            raw = parsed;
        }
        catch (JsonException exception)
        {
            return Result<ClaudeCodeAccountRotationConfiguration, string>.Failure("the configuration file " + fullPath + " could not be parsed: " + exception.Message);
        }

        return Result<ClaudeCodeAccountRotationConfiguration, string>.Success(Merge(defaults, raw));
    }

    private static JsonObject ToJson(ClaudeCodeAccountRotationConfiguration configuration) => new()
    {
        ["liveConfigDirectory"] = configuration.LiveConfigDirectory,
        ["stateFilePath"] = configuration.StateFilePath,
        ["profilesRoot"] = configuration.ProfilesRoot,
        ["appDataDirectory"] = configuration.AppDataDirectory,
        ["listenPort"] = configuration.ListenPort,
        ["refreshLockWaitSeconds"] = configuration.RefreshLockWaitBound.TotalSeconds,
        ["claudeExecutable"] = configuration.ClaudeExecutable,
        ["userAgentProductToken"] = configuration.UserAgentProductToken,
    };

    private static ClaudeCodeAccountRotationConfiguration Merge(ClaudeCodeAccountRotationConfiguration defaults, JsonObject raw)
    {
        string liveConfigDirectory = Text(raw, "liveConfigDirectory") ?? defaults.LiveConfigDirectory;
        return new ClaudeCodeAccountRotationConfiguration(
            liveConfigDirectory,
            Text(raw, "stateFilePath") ?? defaults.StateFilePath,
            Text(raw, "profilesRoot") ?? defaults.ProfilesRoot,
            Text(raw, "appDataDirectory") ?? defaults.AppDataDirectory,
            // Always derived: the tee lives inside the live directory, so a file that
            // moves the live directory moves the tee with it. No knob until a layout
            // exists that needs one.
            ConfigurationDefaults.TeePathFor(liveConfigDirectory),
            Number(raw, "listenPort") is double port ? (int)port : defaults.ListenPort,
            Number(raw, "refreshLockWaitSeconds") is double seconds ? TimeSpan.FromSeconds(seconds) : defaults.RefreshLockWaitBound,
            Text(raw, "claudeExecutable") ?? defaults.ClaudeExecutable,
            Text(raw, "userAgentProductToken") ?? defaults.UserAgentProductToken);
    }

    private static string? Text(JsonObject raw, string key) =>
        raw[key] is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static double? Number(JsonObject raw, string key) =>
        raw[key] is JsonValue value && value.TryGetValue(out double number) ? number : null;

    private static bool? Flag(JsonObject raw, string key) =>
        raw[key] is JsonValue value && value.TryGetValue(out bool flag) ? flag : null;
}
