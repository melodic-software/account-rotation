using System.Diagnostics;
using ClaudeCodeAccountRotation.Core;

namespace ClaudeCodeAccountRotation.App.Adapters.Process;

/// <summary>
/// How to start the CLI: the file to execute and the arguments that precede
/// the CLI's own. A native binary runs directly with an argument list. An npm
/// <c>.cmd</c> shim can only run through the command interpreter, and
/// <c>cmd.exe</c> does not parse its command line by the argument-list rules,
/// so <see cref="Shim"/> names the script and the process adapter builds the
/// one quoted command line the interpreter needs; the interpreter itself is
/// the system directory's, never one found on PATH.
/// </summary>
internal sealed record ClaudeExecutable(string FileName, IReadOnlyList<string> ArgumentPrefix, string? Shim = null)
{
    /// <summary>
    /// How every invocation of the CLI is started: never a shell, never a
    /// joined command line except the one the interpreter forces for an npm
    /// shim, and under <paramref name="configDirectory"/> when one is given so
    /// a command can only ever see one account's config root. Standard output
    /// and error are redirected; a caller that also writes to the child
    /// redirects standard input itself.
    /// </summary>
    public ProcessStartInfo StartInfo(IReadOnlyList<string> arguments, string? configDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ProcessStartInfo startInfo = new(FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (Shim is string shim)
        {
            // cmd.exe does not parse its command line by the argument-list rules, and an
            // argument-list entry is quoted only when it holds a space or a quote, so a
            // shim path carrying "&" would be read as a command separator. With /s the
            // interpreter strips the outer quotes and runs the rest; the inner quotes keep
            // the path one operand. A Windows path can never contain a quote itself.
            startInfo.Arguments = "/d /s /c \"\"" + shim + "\" " + string.Join(' ', arguments) + "\"";
        }
        else
        {
            foreach (string argument in ArgumentPrefix.Concat(arguments))
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        if (configDirectory is not null)
        {
            startInfo.Environment["CLAUDE_CONFIG_DIR"] = configDirectory;
        }

        return startInfo;
    }
}

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
