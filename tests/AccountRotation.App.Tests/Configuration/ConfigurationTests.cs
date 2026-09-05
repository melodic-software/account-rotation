using System.Text.Json.Nodes;
using AccountRotation.App.Configuration;
using AccountRotation.Core;
using AccountRotation.Core.Configuration;

namespace AccountRotation.App.Tests.Configuration;

public sealed class ConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "account-rotation-tests", Guid.NewGuid().ToString("N"));
    private readonly string _home;

    public ConfigurationTests()
    {
        _home = Path.Combine(_root, "home");
        Directory.CreateDirectory(_home);
    }

    private AccountRotationConfiguration Defaults(string? claudeConfigDirectory = null) =>
        ConfigurationDefaults.ForUser(_home, Path.Combine(_home, "AppData", "Local"), claudeConfigDirectory);

    [Fact]
    public void DefaultsDeriveEveryPathFromTheUserProfile()
    {
        AccountRotationConfiguration defaults = Defaults();

        defaults.LiveConfigDirectory.ShouldBe(Path.Combine(_home, ".claude"));
        defaults.StateFilePath.ShouldBe(Path.Combine(_home, ".claude.json"));
        defaults.ProfilesRoot.ShouldBe(Path.Combine(_home, ".claude-profiles"));
        defaults.AppDataDirectory.ShouldBe(Path.Combine(_home, "AppData", "Local", "account-rotation"));
        defaults.ListenPort.ShouldBe(48211);
        defaults.RefreshLockWaitBound.ShouldBe(TimeSpan.FromSeconds(10));
        defaults.PatchStateFile.ShouldBeTrue();
    }

    [Fact]
    public void AClaudeConfigDirectoryMovesTheLiveDirectoryAndTheStateFile()
    {
        string configured = Path.Combine(_root, "elsewhere");

        AccountRotationConfiguration defaults = Defaults(configured);

        defaults.LiveConfigDirectory.ShouldBe(configured);
        defaults.StateFilePath.ShouldBe(Path.Combine(configured, ".claude.json"));
    }

    [Fact]
    public async Task FirstRunWritesTheConfigurationWithResolvedDefaults()
    {
        string path = Path.Combine(_root, "appdata", "config.json");

        Result<AccountRotationConfiguration, string> loaded = await ConfigurationFile.LoadOrCreateAsync(path, Defaults(), TestContext.Current.CancellationToken);

        loaded.IsSuccess.ShouldBeTrue(loaded.IsFailure ? loaded.Error : "");
        loaded.Value.ShouldBe(Defaults());
        JsonObject written = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!.AsObject();
        written["profilesRoot"]!.GetValue<string>().ShouldBe(Defaults().ProfilesRoot);
        written["listenPort"]!.GetValue<int>().ShouldBe(48211);
    }

    [Fact]
    public async Task AnExistingFileOverridesDefaultsKeyByKey()
    {
        string path = Path.Combine(_root, "appdata", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """{"listenPort": 5000, "refreshLockWaitSeconds": 3, "claudeExecutable": "C:/tools/claude.exe"}""", TestContext.Current.CancellationToken);

        AccountRotationConfiguration loaded = (await ConfigurationFile.LoadOrCreateAsync(path, Defaults(), TestContext.Current.CancellationToken)).Value;

        loaded.ListenPort.ShouldBe(5000);
        loaded.RefreshLockWaitBound.ShouldBe(TimeSpan.FromSeconds(3));
        loaded.ClaudeExecutable.ShouldBe("C:/tools/claude.exe");
        loaded.ProfilesRoot.ShouldBe(Defaults().ProfilesRoot);
    }

    [Fact]
    public async Task AMalformedFileIsRefusedNamingThePath()
    {
        string path = Path.Combine(_root, "appdata", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not json", TestContext.Current.CancellationToken);

        Result<AccountRotationConfiguration, string> loaded = await ConfigurationFile.LoadOrCreateAsync(path, Defaults(), TestContext.Current.CancellationToken);

        loaded.IsFailure.ShouldBeTrue();
        loaded.Error.ShouldContain(path);
    }

    [Fact]
    public void AProfilesRootOnAnotherVolumeIsRefusedNamingBothPaths()
    {
        AccountRotationConfiguration configuration = Defaults();
        string VolumeOf(string path) => path.StartsWith(configuration.ProfilesRoot, StringComparison.Ordinal) ? "E:" : "C:";

        Result<Unit, string> verdict = ConfigurationValidator.Validate(configuration, _home, VolumeOf, static _ => null);

        verdict.IsFailure.ShouldBeTrue();
        verdict.Error.ShouldContain(configuration.ProfilesRoot);
        verdict.Error.ShouldContain(configuration.LiveConfigDirectory);
    }

    [Fact]
    public void AProfilesRootInsideTheLiveDirectoryIsRefused()
    {
        AccountRotationConfiguration configuration = Defaults() with { ProfilesRoot = Path.Combine(_home, ".claude", "profiles") };

        ConfigurationValidator.Validate(configuration, _home, static _ => "C:", static _ => null).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void AProfilesRootThatIsTheHomeRootIsRefused()
    {
        AccountRotationConfiguration configuration = Defaults() with { ProfilesRoot = _home };

        ConfigurationValidator.Validate(configuration, _home, static _ => "C:", static _ => null).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void AProfilesRootUnderASyncFolderIsRefused()
    {
        string oneDrive = Path.Combine(_home, "OneDrive");
        AccountRotationConfiguration configuration = Defaults() with { ProfilesRoot = Path.Combine(oneDrive, "profiles") };
        string? Environment(string name) => name == "OneDrive" ? oneDrive : null;

        Result<Unit, string> verdict = ConfigurationValidator.Validate(configuration, _home, static _ => "C:", Environment);

        verdict.IsFailure.ShouldBeTrue();
        verdict.Error.ShouldContain("OneDrive");
    }

    [Fact]
    public void ADropboxFolderUnderTheProfileIsRefusedWithoutAnyVariable()
    {
        AccountRotationConfiguration configuration = Defaults() with { ProfilesRoot = Path.Combine(_home, "Dropbox", "claude-profiles") };

        ConfigurationValidator.Validate(configuration, _home, static _ => "C:", static _ => null).Error.ShouldContain("Dropbox");
    }

    [Fact]
    public void TheDefaultsThemselvesValidate()
    {
        ConfigurationValidator.Validate(Defaults(), _home, static _ => "C:", static _ => null).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void StartupArgumentsParseConfigPortVersionAndHelp()
    {
        StartupArguments parsed = StartupArguments.Parse(["--config", "x.json", "--port", "5001"]).Value;
        parsed.ConfigPath.ShouldBe("x.json");
        parsed.Port.ShouldBe(5001);

        StartupArguments.Parse(["--version"]).Value.ShowVersion.ShouldBeTrue();
        StartupArguments.Parse(["--help"]).Value.ShowHelp.ShouldBeTrue();
        StartupArguments.Parse(["--port", "abc"]).IsFailure.ShouldBeTrue();
        StartupArguments.Parse(["--bogus"]).IsFailure.ShouldBeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
