using System.Text.Json.Nodes;
using AccountRotation.App.Adapters.FileSystem;
using AccountRotation.App.Switching;
using AccountRotation.Core;
using AccountRotation.Core.Identity;
using AccountRotation.Core.Ports;
using AccountRotation.Core.Switching;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccountRotation.App.Tests.Switching;

public sealed class LiveDirectorySwitchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "account-rotation-tests", Guid.NewGuid().ToString("N"));
    private readonly string _liveDirectory;
    private readonly string _stateFilePath;
    private readonly string _profilesRoot;
    private readonly string _appData;
    private readonly CannedAuthStatus _cli = new();
    private readonly CredentialMutationGate _gate = new();

    public LiveDirectorySwitchTests()
    {
        _liveDirectory = Path.Combine(_root, "live");
        _stateFilePath = Path.Combine(_root, ".claude.json");
        _profilesRoot = Path.Combine(_root, "profiles");
        _appData = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(_liveDirectory);
        Directory.CreateDirectory(_profilesRoot);
    }

    private static AccountEmail Email(string value) => AccountEmail.Parse(value).Value;

    private static JsonObject AccountJson(string email) => new() { ["accountUuid"] = "uuid-" + email, ["emailAddress"] = email };

    private async Task WriteStateFileAsync(string email, int startups = 7)
    {
        JsonObject state = new() { ["numStartups"] = startups, ["oauthAccount"] = AccountJson(email), ["trailing"] = "kept" };
        await File.WriteAllTextAsync(_stateFilePath, state.ToJsonString(), TestContext.Current.CancellationToken);
    }

    private async Task<string> ParkedProfileAsync(string email, string refreshToken)
    {
        string folder = Path.Combine(_profilesRoot, email);
        await CredentialFiles.WriteAsync(folder, refreshToken, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(folder, "profile.json"), AccountJson(email).ToJsonString(), TestContext.Current.CancellationToken);
        return folder;
    }

    private LiveDirectorySwitch Switch(TimeSpan? lockWait = null, bool patchStateFile = true, TimeSpan? gateTimeout = null)
    {
        SwitchOptions options = new(_liveDirectory, _stateFilePath, _profilesRoot, _appData, lockWait ?? TimeSpan.FromSeconds(2), gateTimeout ?? TimeSpan.FromMilliseconds(200), patchStateFile);
        return new LiveDirectorySwitch(
            new FileSystemCredentialPairStore(_liveDirectory, _profilesRoot, TimeProvider.System),
            new ClaudeStateFile(_stateFilePath),
            new ProfileFolderStore(_profilesRoot),
            new SwitchJournal(_appData),
            _gate,
            _cli,
            new ManagedLoginPolicyReader(Path.Combine(_root, "managed-settings.json"), static () => null, static () => null),
            options,
            TimeProvider.System,
            NullLogger<LiveDirectorySwitch>.Instance);
    }

    private async Task<string?> StateFileEmailAsync()
    {
        var node = JsonNode.Parse(await File.ReadAllTextAsync(_stateFilePath, TestContext.Current.CancellationToken));
        return node?["oauthAccount"]?["emailAddress"]?.GetValue<string>();
    }

    [Fact]
    public async Task SwitchParksTheLivePairUnparksTheTargetAndPatchesTheStateFile()
    {
        await CredentialFiles.WriteAsync(_liveDirectory, "refresh-a", TestContext.Current.CancellationToken);
        await WriteStateFileAsync("a@example.com");
        await ParkedProfileAsync("b@example.com", "refresh-b");
        _cli.Email = "b@example.com";

        Result<SwitchOutcome, SwitchRefusal> result = await Switch().SwitchToAsync(Email("b@example.com"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.ToString() : "");
        result.Value.Now.ShouldBe(Email("b@example.com"));
        result.Value.ParkedAs.ShouldBe(Email("a@example.com"));
        result.Value.IdentityMismatchWarning.ShouldBeFalse();
        (await CredentialFiles.FingerprintAsync(_liveDirectory, TestContext.Current.CancellationToken)).ShouldBe(CredentialFiles.Pair("refresh-b").Fingerprint);
        (await CredentialFiles.FingerprintAsync(Path.Combine(_profilesRoot, "a@example.com"), TestContext.Current.CancellationToken)).ShouldBe(CredentialFiles.Pair("refresh-a").Fingerprint);
        File.Exists(Path.Combine(_profilesRoot, "a@example.com", "profile.json")).ShouldBeTrue();
        (await StateFileEmailAsync()).ShouldBe("b@example.com");
        JsonNode.Parse(await File.ReadAllTextAsync(_stateFilePath, TestContext.Current.CancellationToken))!["trailing"]!.GetValue<string>().ShouldBe("kept");
        File.Exists(Path.Combine(_appData, "state", "switch-journal.json")).ShouldBeFalse();
        Directory.Exists(Path.Combine(_liveDirectory, OAuthRefreshLock.DirectoryName)).ShouldBeFalse();
    }

    [Fact]
    public async Task SwitchWithoutALivePairOnlyUnparks()
    {
        await WriteStateFileAsync("a@example.com");
        await ParkedProfileAsync("b@example.com", "refresh-b");
        _cli.Email = "b@example.com";

        Result<SwitchOutcome, SwitchRefusal> result = await Switch().SwitchToAsync(Email("b@example.com"), TestContext.Current.CancellationToken);

        result.Value.ParkedAs.ShouldBeNull();
        (await CredentialFiles.FingerprintAsync(_liveDirectory, TestContext.Current.CancellationToken)).ShouldBe(CredentialFiles.Pair("refresh-b").Fingerprint);
    }

    [Fact]
    public async Task ACliReportThatDisagreesIsSurfacedAsAWarning()
    {
        await CredentialFiles.WriteAsync(_liveDirectory, "refresh-a", TestContext.Current.CancellationToken);
        await WriteStateFileAsync("a@example.com");
        await ParkedProfileAsync("b@example.com", "refresh-b");
        _cli.Email = "c@example.com";

        Result<SwitchOutcome, SwitchRefusal> result = await Switch().SwitchToAsync(Email("b@example.com"), TestContext.Current.CancellationToken);

        result.Value.IdentityMismatchWarning.ShouldBeTrue();
    }

    [Fact]
    public async Task SwitchRefusesWhileAFreshRefreshLockIsHeldAndMovesNothing()
    {
        await CredentialFiles.WriteAsync(_liveDirectory, "refresh-a", TestContext.Current.CancellationToken);
        await WriteStateFileAsync("a@example.com");
        await ParkedProfileAsync("b@example.com", "refresh-b");
        Directory.CreateDirectory(Path.Combine(_liveDirectory, OAuthRefreshLock.DirectoryName));

        Result<SwitchOutcome, SwitchRefusal> result = await Switch(lockWait: TimeSpan.FromMilliseconds(300)).SwitchToAsync(Email("b@example.com"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(SwitchRefusal.RefreshLockPresent);
        (await CredentialFiles.FingerprintAsync(_liveDirectory, TestContext.Current.CancellationToken)).ShouldBe(CredentialFiles.Pair("refresh-a").Fingerprint);
        (await StateFileEmailAsync()).ShouldBe("a@example.com");
    }

    [Fact]
    public async Task SwitchRefusesAnUnknownTarget()
    {
        await CredentialFiles.WriteAsync(_liveDirectory, "refresh-a", TestContext.Current.CancellationToken);
        await WriteStateFileAsync("a@example.com");

        Result<SwitchOutcome, SwitchRefusal> result = await Switch().SwitchToAsync(Email("nobody@example.com"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(SwitchRefusal.TargetHasNoCredentials);
    }

    [Fact]
    public async Task StartupQuarantinesADuplicateLineageAndBlocksSwitchingUntilCleared()
    {
        await CredentialFiles.WriteAsync(_liveDirectory, "refresh-a", TestContext.Current.CancellationToken);
        await WriteStateFileAsync("a@example.com");
        await ParkedProfileAsync("b@example.com", "refresh-b");
        string duplicateFolder = await ParkedProfileAsync("c@example.com", "refresh-a");

        ReconciliationReport report = await Switch().ReconcileAsync(TestContext.Current.CancellationToken);

        report.Quarantined.ShouldHaveSingleItem().ShouldContain("c@example.com");
        File.Exists(Path.Combine(duplicateFolder, CredentialFiles.FileName)).ShouldBeFalse();
        Directory.GetFiles(Path.Combine(_appData, "quarantine"), "*", SearchOption.AllDirectories).ShouldHaveSingleItem();
        (await CredentialFiles.FingerprintAsync(_liveDirectory, TestContext.Current.CancellationToken)).ShouldBe(CredentialFiles.Pair("refresh-a").Fingerprint);

        Result<SwitchOutcome, SwitchRefusal> blocked = await Switch().SwitchToAsync(Email("b@example.com"), TestContext.Current.CancellationToken);
        blocked.Error.ShouldBe(SwitchRefusal.LiveIdentityUnverified);

        Directory.Delete(Path.Combine(_appData, "quarantine"), recursive: true);
        _cli.Email = "b@example.com";
        (await Switch().SwitchToAsync(Email("b@example.com"), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ACrashBetweenUnparkAndPatchIsReconciledAtStartup()
    {
        // Files as the switch leaves them after the unpark: b's pair is live, a's is parked,
        // but the state file still names a and the journal is open at Unparked.
        await CredentialFiles.WriteAsync(_liveDirectory, "refresh-b", TestContext.Current.CancellationToken);
        await WriteStateFileAsync("a@example.com");
        string folderA = await ParkedProfileAsync("a@example.com", "refresh-a");
        string folderB = Path.Combine(_profilesRoot, "b@example.com");
        Directory.CreateDirectory(folderB);
        await File.WriteAllTextAsync(Path.Combine(folderB, "profile.json"), AccountJson("b@example.com").ToJsonString(), TestContext.Current.CancellationToken);
        SwitchJournal journal = new(_appData);
        await journal.WriteAsync(new SwitchJournalEntry(
            Email("a@example.com"), CredentialFiles.Pair("refresh-a").Fingerprint, folderA,
            Email("b@example.com"), CredentialFiles.Pair("refresh-b").Fingerprint, folderB,
            SwitchStep.Unparked, DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);

        Result<SwitchOutcome, SwitchRefusal> beforeReconcile = await Switch().SwitchToAsync(Email("a@example.com"), TestContext.Current.CancellationToken);
        beforeReconcile.Error.ShouldBe(SwitchRefusal.LiveIdentityUnverified);

        ReconciliationReport report = await Switch().ReconcileAsync(TestContext.Current.CancellationToken);

        report.JournalOutcome.ShouldContain("completed");
        (await StateFileEmailAsync()).ShouldBe("b@example.com");
        (await journal.ReadOpenAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task ACrashAfterParkingIsUnwoundAtStartup()
    {
        // Live is empty, a's pair sits parked, b's pair still parked; journal at Parked.
        await WriteStateFileAsync("a@example.com");
        string folderA = await ParkedProfileAsync("a@example.com", "refresh-a");
        string folderB = await ParkedProfileAsync("b@example.com", "refresh-b");
        await new SwitchJournal(_appData).WriteAsync(new SwitchJournalEntry(
            Email("a@example.com"), CredentialFiles.Pair("refresh-a").Fingerprint, folderA,
            Email("b@example.com"), CredentialFiles.Pair("refresh-b").Fingerprint, folderB,
            SwitchStep.Parked, DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);

        ReconciliationReport report = await Switch().ReconcileAsync(TestContext.Current.CancellationToken);

        report.JournalOutcome.ShouldContain("unwound");
        (await CredentialFiles.FingerprintAsync(_liveDirectory, TestContext.Current.CancellationToken)).ShouldBe(CredentialFiles.Pair("refresh-a").Fingerprint);
        (await CredentialFiles.FingerprintAsync(folderB, TestContext.Current.CancellationToken)).ShouldBe(CredentialFiles.Pair("refresh-b").Fingerprint);
        (await StateFileEmailAsync()).ShouldBe("a@example.com");
    }

    [Fact]
    public async Task AnUnwindWaitsForTheRefreshLockAndMovesNothingWhileASessionHoldsIt()
    {
        // Same half-done switch as above, but a session is mid-refresh: the restore must not
        // race it, so reconciliation reports the block and leaves every file where it is.
        await WriteStateFileAsync("a@example.com");
        string folderA = await ParkedProfileAsync("a@example.com", "refresh-a");
        string folderB = await ParkedProfileAsync("b@example.com", "refresh-b");
        SwitchJournal journal = new(_appData);
        await journal.WriteAsync(new SwitchJournalEntry(
            Email("a@example.com"), CredentialFiles.Pair("refresh-a").Fingerprint, folderA,
            Email("b@example.com"), CredentialFiles.Pair("refresh-b").Fingerprint, folderB,
            SwitchStep.Parked, DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(_liveDirectory, OAuthRefreshLock.DirectoryName));

        ReconciliationReport report = await Switch(lockWait: TimeSpan.FromMilliseconds(300)).ReconcileAsync(TestContext.Current.CancellationToken);

        report.SwitchingBlocked.ShouldBeTrue();
        report.JournalOutcome.ShouldContain("not unwound yet");
        (await CredentialFiles.FingerprintAsync(_liveDirectory, TestContext.Current.CancellationToken)).ShouldBeNull();
        (await CredentialFiles.FingerprintAsync(folderA, TestContext.Current.CancellationToken)).ShouldBe(CredentialFiles.Pair("refresh-a").Fingerprint);
        (await journal.ReadOpenAsync(TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    [Fact]
    public async Task ConcurrentSwitchesSerializeAndLeaveOneHolderPerLineage()
    {
        await CredentialFiles.WriteAsync(_liveDirectory, "refresh-a", TestContext.Current.CancellationToken);
        await WriteStateFileAsync("a@example.com");
        await ParkedProfileAsync("b@example.com", "refresh-b");
        await ParkedProfileAsync("c@example.com", "refresh-c");
        // A zero gate wait is the endpoint's posture: a second switch during one is a 409, not a queue.
        LiveDirectorySwitch executor = Switch(gateTimeout: TimeSpan.Zero);
        _cli.Email = "b@example.com";

        Task<Result<SwitchOutcome, SwitchRefusal>> toB = executor.SwitchToAsync(Email("b@example.com"), TestContext.Current.CancellationToken);
        Task<Result<SwitchOutcome, SwitchRefusal>> toC = executor.SwitchToAsync(Email("c@example.com"), TestContext.Current.CancellationToken);
        Result<SwitchOutcome, SwitchRefusal>[] results = await Task.WhenAll(toB, toC);

        results.Count(static result => result.IsSuccess).ShouldBe(1);
        results.Single(static result => result.IsFailure).Error.ShouldBe(SwitchRefusal.MutationInProgress);
        List<RefreshTokenFingerprint> fingerprints = [];
        foreach (string directory in new[] { _liveDirectory, Path.Combine(_profilesRoot, "a@example.com"), Path.Combine(_profilesRoot, "b@example.com"), Path.Combine(_profilesRoot, "c@example.com") })
        {
            if (await CredentialFiles.FingerprintAsync(directory, TestContext.Current.CancellationToken) is RefreshTokenFingerprint fingerprint)
            {
                fingerprints.Add(fingerprint);
            }
        }

        fingerprints.Count.ShouldBe(3);
        fingerprints.Distinct().Count().ShouldBe(3);
    }

    public void Dispose()
    {
        _gate.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class CannedAuthStatus : IClaudeCliAuthStatus
    {
        public string? Email { get; set; }

        public Task<Result<ClaudeAuthStatus, string>> ReadAsync(string? configDirectory, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ClaudeAuthStatus, string>.Success(new ClaudeAuthStatus(true, Email, "claude.ai", "Personal", "max", null)));
    }
}
