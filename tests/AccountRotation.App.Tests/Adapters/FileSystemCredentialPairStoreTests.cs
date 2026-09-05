using AccountRotation.App.Adapters.FileSystem;
using AccountRotation.Core;
using AccountRotation.Core.Identity;

namespace AccountRotation.App.Tests.Adapters;

public sealed class FileSystemCredentialPairStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "account-rotation-tests", Guid.NewGuid().ToString("N"));
    private readonly string _liveDirectory;
    private readonly string _profilesRoot;
    private readonly FileSystemCredentialPairStore _store;

    public FileSystemCredentialPairStoreTests()
    {
        _liveDirectory = Path.Combine(_root, "live");
        _profilesRoot = Path.Combine(_root, "profiles");
        Directory.CreateDirectory(_liveDirectory);
        Directory.CreateDirectory(_profilesRoot);
        _store = new FileSystemCredentialPairStore(_liveDirectory, _profilesRoot, TimeProvider.System);
    }

    private string Folder(string name) => Path.Combine(_profilesRoot, name);

    private async Task<(int Files, int Distinct)> HolderCountsAsync(params string[] folders)
    {
        List<RefreshTokenFingerprint> fingerprints = [];
        foreach (string directory in folders.Prepend(_liveDirectory))
        {
            RefreshTokenFingerprint? fingerprint = await CredentialFiles.FingerprintAsync(directory, TestContext.Current.CancellationToken);
            if (fingerprint is RefreshTokenFingerprint present)
            {
                fingerprints.Add(present);
            }
        }

        return (fingerprints.Count, fingerprints.Distinct().Count());
    }

    [Fact]
    public async Task ReadLiveReturnsNullWhenNoPairIsPresent()
    {
        (await _store.ReadLiveAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task ReadLiveParsesThePair()
    {
        await CredentialFiles.WriteAsync(_liveDirectory, "refresh-a", TestContext.Current.CancellationToken);

        CredentialPair? pair = await _store.ReadLiveAsync(TestContext.Current.CancellationToken);

        pair!.Fingerprint.ShouldBe(CredentialFiles.Pair("refresh-a").Fingerprint);
    }

    [Fact]
    public async Task TenSwitchesLeaveEachRefreshTokenInExactlyOneFile()
    {
        await CredentialFiles.WriteAsync(_liveDirectory, "refresh-a", TestContext.Current.CancellationToken);
        await CredentialFiles.WriteAsync(Folder("b"), "refresh-b", TestContext.Current.CancellationToken);
        await CredentialFiles.WriteAsync(Folder("c"), "refresh-c", TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Folder("a"));
        string[] folders = [Folder("a"), Folder("b"), Folder("c")];
        string liveOwner = Folder("a");

        for (int switchIndex = 0; switchIndex < 10; switchIndex++)
        {
            string target = folders[(switchIndex + 1) % folders.Length];
            await _store.MoveLiveToParkedAsync(liveOwner, TestContext.Current.CancellationToken);
            await _store.MoveParkedToLiveAsync(target, TestContext.Current.CancellationToken);
            liveOwner = target;

            (int files, int distinct) = await HolderCountsAsync(folders);
            files.ShouldBe(3);
            distinct.ShouldBe(3);
        }
    }

    [Fact]
    public async Task ParkRefusesWhenTheFolderAlreadyHoldsAPair()
    {
        await CredentialFiles.WriteAsync(_liveDirectory, "refresh-a", TestContext.Current.CancellationToken);
        await CredentialFiles.WriteAsync(Folder("a"), "refresh-stale", TestContext.Current.CancellationToken);

        await Should.ThrowAsync<InvalidOperationException>(() => _store.MoveLiveToParkedAsync(Folder("a"), TestContext.Current.CancellationToken));

        (await HolderCountsAsync(Folder("a"))).ShouldBe((2, 2));
    }

    [Fact]
    public async Task UnparkRefusesWhenTheLiveDirectoryAlreadyHoldsAPair()
    {
        await CredentialFiles.WriteAsync(_liveDirectory, "refresh-a", TestContext.Current.CancellationToken);
        await CredentialFiles.WriteAsync(Folder("b"), "refresh-b", TestContext.Current.CancellationToken);

        await Should.ThrowAsync<InvalidOperationException>(() => _store.MoveParkedToLiveAsync(Folder("b"), TestContext.Current.CancellationToken));

        (await HolderCountsAsync(Folder("b"))).ShouldBe((2, 2));
    }

    [Fact]
    public async Task UnparkSetsTheLiveFileModificationTimeToNow()
    {
        await CredentialFiles.WriteAsync(Folder("b"), "refresh-b", TestContext.Current.CancellationToken);
        string parkedPath = Path.Combine(Folder("b"), CredentialFiles.FileName);
        File.SetLastWriteTimeUtc(parkedPath, DateTime.UtcNow.AddHours(-1));

        await _store.MoveParkedToLiveAsync(Folder("b"), TestContext.Current.CancellationToken);

        DateTime liveWriteTime = File.GetLastWriteTimeUtc(Path.Combine(_liveDirectory, CredentialFiles.FileName));
        (DateTime.UtcNow - liveWriteTime).ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task WriteParkedRefusesWhenTheFileNoLongerHoldsTheExpectedPair()
    {
        await CredentialFiles.WriteAsync(Folder("b"), "refresh-b", TestContext.Current.CancellationToken);
        CredentialPair rotated = CredentialFiles.Pair("refresh-b2");

        Result<Unit, string> result = await _store.WriteParkedAsync(
            Folder("b"), rotated, CredentialFiles.Pair("refresh-other").Fingerprint, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        (await CredentialFiles.FingerprintAsync(Folder("b"), TestContext.Current.CancellationToken)).ShouldBe(CredentialFiles.Pair("refresh-b").Fingerprint);
    }

    [Fact]
    public async Task WriteParkedReplacesThePairWhenTheFingerprintMatches()
    {
        await CredentialFiles.WriteAsync(Folder("b"), "refresh-b", TestContext.Current.CancellationToken);
        CredentialPair rotated = CredentialFiles.Pair("refresh-b2");

        Result<Unit, string> result = await _store.WriteParkedAsync(
            Folder("b"), rotated, CredentialFiles.Pair("refresh-b").Fingerprint, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        (await CredentialFiles.FingerprintAsync(Folder("b"), TestContext.Current.CancellationToken)).ShouldBe(rotated.Fingerprint);
    }

    [Fact]
    public async Task FreshLockFileNameIgnoresTheDaemonLockAndOldFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_liveDirectory, "daemon.lock"), "", TestContext.Current.CancellationToken);
        string oldLock = Path.Combine(_liveDirectory, "old.lock");
        await File.WriteAllTextAsync(oldLock, "", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(oldLock, DateTime.UtcNow.AddMinutes(-2));

        _store.FreshLockFileName(TimeSpan.FromSeconds(60)).ShouldBeNull();

        await File.WriteAllTextAsync(Path.Combine(_liveDirectory, "refresh.lock"), "", TestContext.Current.CancellationToken);
        _store.FreshLockFileName(TimeSpan.FromSeconds(60)).ShouldBe("refresh.lock");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
