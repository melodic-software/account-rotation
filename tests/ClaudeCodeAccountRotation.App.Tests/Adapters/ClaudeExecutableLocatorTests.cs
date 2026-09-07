using ClaudeCodeAccountRotation.App.Adapters.Process;
using ClaudeCodeAccountRotation.Core;

namespace ClaudeCodeAccountRotation.App.Tests.Adapters;

public sealed class ClaudeExecutableLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "claude-code-account-rotation-tests", Guid.NewGuid().ToString("N"));

    public ClaudeExecutableLocatorTests() => Directory.CreateDirectory(_root);

    private string Touch(string relativePath)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void AConfiguredExecutableWinsWhenItExists()
    {
        string configured = Touch(Path.Combine("custom", "claude.exe"));

        ClaudeExecutable located = ClaudeExecutableLocator.Locate(configured, pathVariable: null, isWindows: true).Value;

        located.FileName.ShouldBe(configured);
        located.ArgumentPrefix.ShouldBeEmpty();
    }

    [Fact]
    public void AConfiguredExecutableThatDoesNotExistIsRefusedNotSkipped()
    {
        Result<ClaudeExecutable, string> result = ClaudeExecutableLocator.Locate(Path.Combine(_root, "missing.exe"), pathVariable: null, isWindows: true);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("missing.exe");
    }

    [Fact]
    public void ANativeBinaryOnPathIsPreferredOverAnNpmShim()
    {
        string shimDirectory = Path.GetDirectoryName(Touch(Path.Combine("npm", "claude.cmd")))!;
        string nativeDirectory = Path.GetDirectoryName(Touch(Path.Combine("local", "claude.exe")))!;
        string pathVariable = shimDirectory + Path.PathSeparator + nativeDirectory;

        ClaudeExecutable located = ClaudeExecutableLocator.Locate(null, pathVariable, isWindows: true).Value;

        located.FileName.ShouldBe(Path.Combine(nativeDirectory, "claude.exe"));
        located.ArgumentPrefix.ShouldBeEmpty();
    }

    [Fact]
    public void AnNpmShimRunsThroughTheSystemCommandInterpreterAsAShim()
    {
        string shimPath = Touch(Path.Combine("npm", "claude.cmd"));
        string systemDirectory = Path.Combine(_root, "system32");

        ClaudeExecutable located = ClaudeExecutableLocator.Locate(null, Path.GetDirectoryName(shimPath), isWindows: true, systemDirectory).Value;

        located.FileName.ShouldBe(Path.Combine(systemDirectory, "cmd.exe"), "the interpreter is named by full path, never resolved through PATH");
        located.ArgumentPrefix.ShouldBeEmpty();
        located.Shim.ShouldBe(shimPath);
    }

    [Fact]
    public void OnUnixTheBareNameIsResolvedOnPath()
    {
        string binary = Touch(Path.Combine("bin", "claude"));

        ClaudeExecutable located = ClaudeExecutableLocator.Locate(null, Path.GetDirectoryName(binary), isWindows: false).Value;

        located.FileName.ShouldBe(binary);
    }

    [Fact]
    public void NothingResolvableIsRefusedWithGuidance()
    {
        Result<ClaudeExecutable, string> result = ClaudeExecutableLocator.Locate(null, Path.Combine(_root, "empty"), isWindows: true);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("claudeExecutable");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
