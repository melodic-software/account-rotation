using System.Text.Json.Nodes;
using AccountRotation.App.Adapters.FileSystem;

namespace AccountRotation.App.Tests.Adapters;

public sealed class AtomicJsonFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "account-rotation-tests", Guid.NewGuid().ToString("N"));

    public AtomicJsonFileTests() => Directory.CreateDirectory(_directory);

    public static bool OnWindows => OperatingSystem.IsWindows();

    [Fact]
    public async Task WritesANewFileAndLeavesNoTemporaryResidue()
    {
        string path = Path.Combine(_directory, "state.json");

        await AtomicJsonFile.WriteAsync(path, new JsonObject { ["a"] = 1 }, TestContext.Current.CancellationToken);

        JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!["a"]!.GetValue<int>().ShouldBe(1);
        Directory.GetFiles(_directory).ShouldBe([path]);
    }

    [Fact]
    public async Task ReplacesAnExistingFileWhole()
    {
        string path = Path.Combine(_directory, "state.json");
        await File.WriteAllTextAsync(path, """{"old":true,"padding":"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"}""", TestContext.Current.CancellationToken);

        await AtomicJsonFile.WriteAsync(path, new JsonObject { ["fresh"] = true }, TestContext.Current.CancellationToken);

        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ShouldBe("""{"fresh":true}""");
        Directory.GetFiles(_directory).ShouldBe([path]);
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Sharing violations are a Windows behavior")]
    public async Task RetriesWhileAnotherHandleHoldsTheTargetBriefly()
    {
        string path = Path.Combine(_directory, "state.json");
        await File.WriteAllTextAsync(path, """{"old":true}""", TestContext.Current.CancellationToken);
        TaskCompletionSource opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = Task.Run(async () =>
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
            opened.SetResult();
            await Task.Delay(300, TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);
        await opened.Task;

        await AtomicJsonFile.WriteAsync(path, new JsonObject { ["fresh"] = true }, TestContext.Current.CancellationToken);
        await holder;

        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ShouldBe("""{"fresh":true}""");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
