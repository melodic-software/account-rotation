using AccountRotation.Core;

namespace AccountRotation.App.Adapters.Process;

/// <summary>
/// How to start the CLI: the file to execute and the arguments that precede
/// the CLI's own. A native binary runs directly; an npm <c>.cmd</c> shim runs
/// through the command interpreter with an argument list, the one place a
/// shell is involved, and never a joined command string.
/// </summary>
internal sealed record ClaudeExecutable(string FileName, IReadOnlyList<string> ArgumentPrefix);

/// <summary>
/// Resolves the <c>claude</c> executable: the <c>claudeExecutable</c> configuration
/// key when set (an absent file there is refused, not skipped), else the first
/// PATH entry holding a native binary, else the first holding an npm shim.
/// </summary>
internal static class ClaudeExecutableLocator
{
    public static Result<ClaudeExecutable, string> Locate(string? configuredExecutable, string? pathVariable, bool isWindows)
    {
        if (!string.IsNullOrWhiteSpace(configuredExecutable))
        {
            string configured = Path.GetFullPath(configuredExecutable);
            return File.Exists(configured)
                ? Result<ClaudeExecutable, string>.Success(Describe(configured, isWindows))
                : Result<ClaudeExecutable, string>.Failure("the configured claudeExecutable does not exist: " + configured);
        }

        string[] directories = (pathVariable ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] nativeNames = isWindows ? ["claude.exe"] : ["claude"];
        string[] shimNames = isWindows ? ["claude.cmd"] : [];

        foreach (string[] candidates in new[] { nativeNames, shimNames })
        {
            foreach (string directory in directories)
            {
                foreach (string candidate in candidates)
                {
                    string path = Path.Combine(directory, candidate);
                    if (File.Exists(path))
                    {
                        return Result<ClaudeExecutable, string>.Success(Describe(path, isWindows));
                    }
                }
            }
        }

        return Result<ClaudeExecutable, string>.Failure(
            "no claude executable was found on PATH; set the claudeExecutable configuration key to its full path");
    }

    private static ClaudeExecutable Describe(string path, bool isWindows)
    {
        bool isShim = isWindows && Path.GetExtension(path).Equals(".cmd", StringComparison.OrdinalIgnoreCase);
        return isShim
            ? new ClaudeExecutable("cmd.exe", ["/c", path])
            : new ClaudeExecutable(path, []);
    }
}
