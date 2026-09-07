using System.Diagnostics;
using ClaudeCodeAccountRotation.Core;
using ClaudeCodeAccountRotation.Core.Accounts;
using ClaudeCodeAccountRotation.Core.Ports;

namespace ClaudeCodeAccountRotation.App.Adapters.Process;

/// <summary>
/// Opens a URL in one Chromium-family browser profile. The executable comes
/// from the <c>browserExecutables</c> configuration overrides first (an absent
/// file there is refused, not skipped, as with the CLI), then from the
/// platform's known install locations. Every argument is passed as an
/// argument-list entry and never as a joined command line, so a URL carrying a
/// shell metacharacter is one operand rather than a second command.
/// </summary>
internal sealed class ChromiumFamilyBrowserLauncher : IBrowserLauncher
{
    private readonly IReadOnlyDictionary<string, string> _overrides;
    private readonly Func<string, bool> _fileExists;
    private readonly Action<string, IReadOnlyList<string>> _start;

    public ChromiumFamilyBrowserLauncher(
        IReadOnlyDictionary<string, string>? overrides = null,
        Func<string, bool>? fileExists = null,
        Action<string, IReadOnlyList<string>>? start = null)
    {
        _overrides = overrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _fileExists = fileExists ?? File.Exists;
        _start = start ?? StartProcess;
    }

    public Result<Unit, string> Launch(BrowserFamily browser, string? profileDirectory, Uri signInUrl)
    {
        ArgumentNullException.ThrowIfNull(signInUrl);
        Result<string, string> executable = Resolve(browser);
        if (executable.IsFailure)
        {
            return Result<Unit, string>.Failure(executable.Error);
        }

        List<string> arguments = [];
        if (!string.IsNullOrWhiteSpace(profileDirectory))
        {
            arguments.Add("--profile-directory=" + profileDirectory);
        }

        arguments.Add(signInUrl.AbsoluteUri);

        try
        {
            _start(executable.Value, arguments);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            return Result<Unit, string>.Failure("could not start " + executable.Value + ": " + exception.Message);
        }

        return Result<Unit, string>.Success(Unit.Value);
    }

    /// <summary>The candidate install locations for a browser on this platform, most likely first.</summary>
    internal static IReadOnlyList<string> KnownInstallPaths(BrowserFamily browser)
    {
        if (OperatingSystem.IsWindows())
        {
            (string vendor, string executable) = browser switch
            {
                BrowserFamily.Chrome => (Path.Combine("Google", "Chrome"), "chrome.exe"),
                BrowserFamily.Edge => (Path.Combine("Microsoft", "Edge"), "msedge.exe"),
                _ => (Path.Combine("BraveSoftware", "Brave-Browser"), "brave.exe"),
            };
            Environment.SpecialFolder[] roots =
            [
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.LocalApplicationData,
            ];
            return [.. roots
                .Select(Environment.GetFolderPath)
                .Where(static root => !string.IsNullOrWhiteSpace(root))
                .Select(root => Path.Combine(root, vendor, "Application", executable))];
        }

        if (OperatingSystem.IsMacOS())
        {
            string bundle = browser switch
            {
                BrowserFamily.Chrome => "Google Chrome",
                BrowserFamily.Edge => "Microsoft Edge",
                _ => "Brave Browser",
            };
            return [Path.Combine("/Applications", bundle + ".app", "Contents", "MacOS", bundle)];
        }

        return browser switch
        {
            BrowserFamily.Chrome => ["/usr/bin/google-chrome", "/usr/bin/google-chrome-stable", "/opt/google/chrome/chrome"],
            BrowserFamily.Edge => ["/usr/bin/microsoft-edge", "/usr/bin/microsoft-edge-stable"],
            _ => ["/usr/bin/brave-browser", "/usr/bin/brave"],
        };
    }

    private static void StartProcess(string fileName, IReadOnlyList<string> arguments)
    {
        ProcessStartInfo startInfo = new(fileName) { UseShellExecute = false };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var started = System.Diagnostics.Process.Start(startInfo);
        // The browser outlives this call; nothing here waits on it or reads it.
    }

    private Result<string, string> Resolve(BrowserFamily browser)
    {
        string key = browser.ToString().ToLowerInvariant();
        if (_overrides.TryGetValue(key, out string? configured) && !string.IsNullOrWhiteSpace(configured))
        {
            string full = Path.GetFullPath(configured);
            return _fileExists(full)
                ? Result<string, string>.Success(full)
                : Result<string, string>.Failure("the configured browserExecutables." + key + " does not exist: " + full);
        }

        foreach (string candidate in KnownInstallPaths(browser))
        {
            if (_fileExists(candidate))
            {
                return Result<string, string>.Success(candidate);
            }
        }

        return Result<string, string>.Failure(
            "no " + key + " installation was found; set browserExecutables." + key + " in the configuration file to its full path");
    }
}
