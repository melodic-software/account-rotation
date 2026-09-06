using AccountRotation.Core;

namespace AccountRotation.App.Adapters.Process;

/// <summary>
/// How to start the CLI: the file to execute and the arguments that precede
/// the CLI's own. A native binary runs directly with an argument list. An npm
/// <c>.cmd</c> shim can only run through the command interpreter, and
/// <c>cmd.exe</c> does not parse its command line by the argument-list rules,
/// so <see cref="Shim"/> names the script and the process adapter builds the
/// one quoted command line the interpreter needs; the interpreter itself is
/// the system directory's, never one found on PATH.
/// </summary>
internal sealed record ClaudeExecutable(string FileName, IReadOnlyList<string> ArgumentPrefix, string? Shim = null);

/// <summary>
/// Resolves the <c>claude</c> executable: the <c>claudeExecutable</c> configuration
/// key when set (an absent file there is refused, not skipped), else the first
/// PATH entry holding a native binary, else the first holding an npm shim.
/// </summary>
internal static class ClaudeExecutableLocator
{
    /// <param name="systemDirectory">Where the command interpreter lives (<see cref="Environment.SystemDirectory"/> on a real Windows machine); only consulted for an npm shim.</param>
    public static Result<ClaudeExecutable, string> Locate(string? configuredExecutable, string? pathVariable, bool isWindows, string? systemDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredExecutable))
        {
            string configured = Path.GetFullPath(configuredExecutable);
            return File.Exists(configured)
                ? Result<ClaudeExecutable, string>.Success(Describe(configured, isWindows, systemDirectory))
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
                        return Result<ClaudeExecutable, string>.Success(Describe(path, isWindows, systemDirectory));
                    }
                }
            }
        }

        return Result<ClaudeExecutable, string>.Failure(
            "no claude executable was found on PATH; set the claudeExecutable configuration key to its full path");
    }

    private static ClaudeExecutable Describe(string path, bool isWindows, string? systemDirectory)
    {
        bool isShim = isWindows && Path.GetExtension(path).Equals(".cmd", StringComparison.OrdinalIgnoreCase);
        return isShim
            ? new ClaudeExecutable(SystemCommandInterpreter(systemDirectory), [], Shim: path)
            : new ClaudeExecutable(path, []);
    }

    /// <summary>The interpreter from the system directory, by full path, never a bare name resolved through PATH or the current directory.</summary>
    private static string SystemCommandInterpreter(string? systemDirectory)
    {
        string directory = string.IsNullOrWhiteSpace(systemDirectory) ? Environment.SystemDirectory : systemDirectory;
        return Path.Combine(directory, "cmd.exe");
    }
}
