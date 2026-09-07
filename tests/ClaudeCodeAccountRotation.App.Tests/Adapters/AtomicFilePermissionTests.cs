using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using ClaudeCodeAccountRotation.App.Adapters.FileSystem;

namespace ClaudeCodeAccountRotation.App.Tests.Adapters;

/// <summary>
/// The files this writer creates hold credential pairs, so they are readable by
/// their owner and by nobody else, on both legs. Only one leg runs on any given
/// machine; CI runs the pair.
/// </summary>
public sealed class AtomicFilePermissionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "claude-code-account-rotation-tests", Guid.NewGuid().ToString("N"));

    public AtomicFilePermissionTests() => Directory.CreateDirectory(_directory);

    public static bool OnWindows => OperatingSystem.IsWindows();

    public static bool OnUnix => !OperatingSystem.IsWindows();

    [Fact]
    public async Task ReplacesAbsentTargetByMove()
    {
        string path = Path.Combine(_directory, "state.json");

        await AtomicJsonFile.WriteAsync(path, new JsonObject { ["fresh"] = true }, TestContext.Current.CancellationToken);

        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ShouldBe("""{"fresh":true}""");
        Directory.GetFiles(_directory).ShouldBe([path]);
    }

    [Fact(SkipUnless = nameof(OnUnix), Skip = "File modes are a Unix behavior")]
    public async Task CreatesOwnerOnlyFilesOnUnix()
    {
        string path = Path.Combine(_directory, "state.json");

        await AtomicJsonFile.WriteAsync(path, new JsonObject { ["a"] = 1 }, TestContext.Current.CancellationToken);

        // The guard is for the analyzer; the attribute above keeps the leg off Windows.
        if (!OperatingSystem.IsWindows())
        {
            UnixMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Directory.GetFiles(_directory).ShouldBe([path]);
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Access control lists are a Windows behavior")]
    public async Task CreatesOwnerOnlyFilesOnWindows()
    {
        string path = Path.Combine(_directory, "state.json");

        await AtomicJsonFile.WriteAsync(path, new JsonObject { ["a"] = 1 }, TestContext.Current.CancellationToken);

        if (OperatingSystem.IsWindows())
        {
            GrantedIdentities(path).ShouldBe([CurrentUserSid()]);
        }

        Directory.GetFiles(_directory).ShouldBe([path]);
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static UnixFileMode UnixMode(string path) => File.GetUnixFileMode(path);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string CurrentUserSid() => WindowsIdentity.GetCurrent().User!.Value;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> GrantedIdentities(string path) =>
    [
        .. new FileInfo(path).GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => rule.IdentityReference.Value),
    ];

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
