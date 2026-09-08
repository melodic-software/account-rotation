using ClaudeCodeAccountRotation.App.Adapters.Process;
using ClaudeCodeAccountRotation.Core;
using ClaudeCodeAccountRotation.Core.Accounts;

namespace ClaudeCodeAccountRotation.App.Tests.Adapters;

public sealed class ChromiumFamilyBrowserLauncherTests
{
    private static readonly Uri _signInUrl = new("https://claude.ai/login?login_hint=a%40example.com");

    private sealed class Recorder
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Started { get; } = [];

        public void Start(string fileName, IReadOnlyList<string> arguments) => Started.Add((fileName, arguments));
    }

    private static Dictionary<string, string> ChromeAt(string path) =>
        new(StringComparer.OrdinalIgnoreCase) { ["chrome"] = path };

    [Fact]
    public void ChromeWithAProfileIsStartedWithTheProfileDirectoryThenTheUrl()
    {
        Recorder recorder = new();
        string chrome = Path.Combine(Path.GetTempPath(), "chrome.exe");
        ChromiumFamilyBrowserLauncher launcher = new(ChromeAt(chrome), _ => true, recorder.Start);

        Result<Unit, string> launched = launcher.Launch(BrowserFamily.Chrome, "Profile 3", _signInUrl);

        launched.IsSuccess.ShouldBeTrue();
        recorder.Started.Count.ShouldBe(1);
        recorder.Started[0].FileName.ShouldBe(Path.GetFullPath(chrome));
        recorder.Started[0].Arguments.ShouldBe(["--profile-directory=Profile 3", _signInUrl.AbsoluteUri]);
    }

    [Fact]
    public void NoProfileDirectoryPassesTheUrlAlone()
    {
        Recorder recorder = new();
        ChromiumFamilyBrowserLauncher launcher = new(ChromeAt(Path.Combine(Path.GetTempPath(), "chrome.exe")), _ => true, recorder.Start);

        launcher.Launch(BrowserFamily.Chrome, profileDirectory: null, _signInUrl).IsSuccess.ShouldBeTrue();

        recorder.Started[0].Arguments.ShouldBe([_signInUrl.AbsoluteUri]);
    }

    [Fact]
    public void AConfiguredExecutableThatDoesNotExistIsRefusedRatherThanSkipped()
    {
        Recorder recorder = new();
        ChromiumFamilyBrowserLauncher launcher = new(ChromeAt(Path.Combine(Path.GetTempPath(), "absent-chrome.exe")), _ => false, recorder.Start);

        Result<Unit, string> launched = launcher.Launch(BrowserFamily.Chrome, "Profile 3", _signInUrl);

        launched.IsFailure.ShouldBeTrue();
        launched.Error.ShouldContain("browserExecutables.chrome");
        recorder.Started.ShouldBeEmpty();
    }

    [Fact]
    public void WithNoOverrideThePlatformsKnownInstallPathIsUsed()
    {
        Recorder recorder = new();
        string expected = ChromiumFamilyBrowserLauncher.KnownInstallPaths(BrowserFamily.Edge)[0];
        ChromiumFamilyBrowserLauncher launcher = new(overrides: null, path => path == expected, recorder.Start);

        launcher.Launch(BrowserFamily.Edge, "Default", _signInUrl).IsSuccess.ShouldBeTrue();

        recorder.Started[0].FileName.ShouldBe(expected);
    }

    [Fact]
    public void NoInstallationFoundNamesTheConfigurationKey()
    {
        Recorder recorder = new();
        ChromiumFamilyBrowserLauncher launcher = new(overrides: null, _ => false, recorder.Start);

        Result<Unit, string> launched = launcher.Launch(BrowserFamily.Brave, "Default", _signInUrl);

        launched.IsFailure.ShouldBeTrue();
        launched.Error.ShouldContain("browserExecutables.brave");
        recorder.Started.ShouldBeEmpty();
    }
}
