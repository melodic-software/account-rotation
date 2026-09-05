using System.Diagnostics;
using AccountRotation.App.Adapters.FileSystem;
using AccountRotation.Core;

namespace AccountRotation.App.Tests.Adapters;

public sealed class OAuthRefreshLockTests : IDisposable
{
    private readonly string _liveDirectory = Path.Combine(Path.GetTempPath(), "account-rotation-tests", Guid.NewGuid().ToString("N"));
    private readonly string _lockDirectory;

    public OAuthRefreshLockTests()
    {
        Directory.CreateDirectory(_liveDirectory);
        _lockDirectory = Path.Combine(_liveDirectory, OAuthRefreshLock.DirectoryName);
    }

    [Fact]
    public async Task AcquireCreatesTheLockDirectoryAndDisposeRemovesIt()
    {
        OAuthRefreshLock refreshLock = new(_liveDirectory, TimeProvider.System);

        Result<IAsyncDisposable, string> held = await refreshLock.AcquireAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        held.IsSuccess.ShouldBeTrue();
        Directory.Exists(_lockDirectory).ShouldBeTrue();
        await held.Value.DisposeAsync();
        Directory.Exists(_lockDirectory).ShouldBeFalse();
    }

    [Fact]
    public async Task WaitsForTheBoundThenRefusesWhileAFreshLockIsHeld()
    {
        Directory.CreateDirectory(_lockDirectory);
        OAuthRefreshLock refreshLock = new(_liveDirectory, TimeProvider.System);
        var stopwatch = Stopwatch.StartNew();

        Result<IAsyncDisposable, string> held = await refreshLock.AcquireAsync(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);

        held.IsFailure.ShouldBeTrue();
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(350));
        Directory.Exists(_lockDirectory).ShouldBeTrue();
    }

    [Fact]
    public async Task StealsALockDirectoryOlderThanTheStaleThreshold()
    {
        Directory.CreateDirectory(_lockDirectory);
        Directory.SetLastWriteTimeUtc(_lockDirectory, DateTime.UtcNow.AddSeconds(-61));
        OAuthRefreshLock refreshLock = new(_liveDirectory, TimeProvider.System);

        Result<IAsyncDisposable, string> held = await refreshLock.AcquireAsync(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);

        held.IsSuccess.ShouldBeTrue();
        await held.Value.DisposeAsync();
        Directory.Exists(_lockDirectory).ShouldBeFalse();
    }

    [Fact]
    public async Task TwoAcquisitionsSerialize()
    {
        OAuthRefreshLock refreshLock = new(_liveDirectory, TimeProvider.System);
        Result<IAsyncDisposable, string> first = await refreshLock.AcquireAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        first.IsSuccess.ShouldBeTrue();

        Task<Result<IAsyncDisposable, string>> second = refreshLock.AcquireAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        second.IsCompleted.ShouldBeFalse();
        await first.Value.DisposeAsync();

        Result<IAsyncDisposable, string> held = await second;
        held.IsSuccess.ShouldBeTrue();
        await held.Value.DisposeAsync();
        Directory.Exists(_lockDirectory).ShouldBeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_liveDirectory))
        {
            Directory.Delete(_liveDirectory, recursive: true);
        }
    }
}
