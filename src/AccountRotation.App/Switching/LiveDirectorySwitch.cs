using System.Globalization;
using System.Text.Json.Nodes;
using AccountRotation.App.Adapters.FileSystem;
using AccountRotation.Core;
using AccountRotation.Core.Identity;
using AccountRotation.Core.Ports;
using AccountRotation.Core.Switching;
using Microsoft.Extensions.Logging;

namespace AccountRotation.App.Switching;

/// <summary>
/// Executes a switch: snapshot the live directory, plan, take Claude Code's own
/// refresh lock, journal the intent, park, unpark, patch the state file, clear
/// the journal, then verify with the CLI. Every step that moves a credential
/// runs under the one mutation gate and inside the refresh lock. Startup
/// reconciliation quarantines duplicate lineages and finishes or unwinds a
/// switch a crash left half done.
/// </summary>
internal sealed partial class LiveDirectorySwitch
{
    private const string QuarantineDirectoryName = "quarantine";
    private const string LiveOwnerFileName = "live-owner.json";
    private static readonly TimeSpan _secondaryLockGuardAge = TimeSpan.FromSeconds(60);

    private readonly ICredentialPairStore _pairs;
    private readonly ClaudeStateFile _stateFile;
    private readonly ProfileFolderStore _profiles;
    private readonly SwitchJournal _journal;
    private readonly CredentialMutationGate _gate;
    private readonly IClaudeCliAuthStatus _authStatus;
    private readonly ManagedLoginPolicyReader _policyReader;
    private readonly SwitchOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LiveDirectorySwitch> _logger;
    private readonly string _quarantineDirectory;
    private readonly string _liveOwnerPath;

    public LiveDirectorySwitch(
        ICredentialPairStore pairs,
        ClaudeStateFile stateFile,
        ProfileFolderStore profiles,
        SwitchJournal journal,
        CredentialMutationGate gate,
        IClaudeCliAuthStatus authStatus,
        ManagedLoginPolicyReader policyReader,
        SwitchOptions options,
        TimeProvider timeProvider,
        ILogger<LiveDirectorySwitch> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _pairs = pairs;
        _stateFile = stateFile;
        _profiles = profiles;
        _journal = journal;
        _gate = gate;
        _authStatus = authStatus;
        _policyReader = policyReader;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _quarantineDirectory = Path.Combine(options.AppDataDirectory, QuarantineDirectoryName);
        _liveOwnerPath = Path.Combine(options.AppDataDirectory, "state", LiveOwnerFileName);
    }

    public async Task<Result<SwitchOutcome, SwitchRefusal>> SwitchToAsync(AccountEmail target, CancellationToken cancellationToken)
    {
        IDisposable? permit = null;
        try
        {
            try
            {
                permit = await _gate.AcquireAsync(_options.MutationGateTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                LogRefused(target.Value, SwitchRefusal.MutationInProgress);
                return Result<SwitchOutcome, SwitchRefusal>.Failure(SwitchRefusal.MutationInProgress);
            }

            return await SwitchUnderGateAsync(target, cancellationToken);
        }
        finally
        {
            permit?.Dispose();
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(CancellationToken cancellationToken)
    {
        using IDisposable permit = await _gate.AcquireAsync(_options.MutationGateTimeout, cancellationToken);
        IReadOnlyList<string> quarantined = await QuarantineDuplicateLineagesAsync(cancellationToken);
        (string journalOutcome, bool journalBlocks) = await ReconcileJournalAsync(cancellationToken);
        bool quarantineBlocks = QuarantineHoldsFiles();
        string? banner = quarantineBlocks
            ? "A duplicate credential lineage was quarantined under " + _quarantineDirectory + "; delete or restore those files before switching."
            : journalBlocks ? "An earlier switch could not be reconciled: " + journalOutcome : null;
        return new ReconciliationReport(quarantined, journalOutcome, quarantineBlocks || journalBlocks, banner);
    }

    private async Task<Result<SwitchOutcome, SwitchRefusal>> SwitchUnderGateAsync(AccountEmail target, CancellationToken cancellationToken)
    {
        LiveAccountState live = await SnapshotLiveAsync(cancellationToken);
        IReadOnlyList<ParkedProfile> profiles = await _profiles.ListAsync(cancellationToken);
        ParkedProfile? targetProfile = profiles.FirstOrDefault(profile => profile.Email == target);
        if (targetProfile is null)
        {
            LogRefused(target.Value, SwitchRefusal.TargetHasNoCredentials);
            return Result<SwitchOutcome, SwitchRefusal>.Failure(SwitchRefusal.TargetHasNoCredentials);
        }

        CredentialPair? liveCredentials = await _pairs.ReadLiveAsync(cancellationToken);
        CredentialPair? targetCredentials = targetProfile.HasCredentials
            ? await _pairs.ReadParkedAsync(targetProfile.FolderPath, cancellationToken)
            : null;
        ManagedLoginPolicy policy = await _policyReader.ReadAsync(cancellationToken);
        bool journalOpen = await _journal.ReadOpenAsync(cancellationToken) is not null || QuarantineHoldsFiles();
        AccountEmail? liveOwner = await ReadLiveOwnerAsync(live.Fingerprint, cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        Result<SwitchPlan, SwitchRefusal> planned = SwitchPlanner.Plan(new SwitchPlanningInput(
            live, targetProfile, liveCredentials, targetCredentials, policy, journalOpen, liveOwner, _options.ProfilesRoot, now));
        if (planned.IsFailure)
        {
            LogRefused(target.Value, planned.Error);
            return Result<SwitchOutcome, SwitchRefusal>.Failure(planned.Error);
        }

        SwitchPlan plan = planned.Value;
        Result<IAsyncDisposable, string> held = await _pairs.AcquireRefreshLockAsync(_options.RefreshLockWaitBound, cancellationToken);
        if (held.IsFailure)
        {
            LogLockRefused(target.Value, held.Error);
            return Result<SwitchOutcome, SwitchRefusal>.Failure(SwitchRefusal.RefreshLockPresent);
        }

        await using (held.Value)
        {
            SwitchJournalEntry entry = new(
                plan.Outgoing, liveCredentials?.Fingerprint, plan.OutgoingFolderPath,
                plan.Incoming, targetCredentials!.Fingerprint, plan.IncomingFolderPath,
                SwitchStep.Planned, now);
            await _journal.WriteAsync(entry, cancellationToken);

            if (plan.Outgoing is AccountEmail outgoing && plan.OutgoingFolderPath is string outgoingFolder)
            {
                await _profiles.EnsureFolderAsync(outgoing, cancellationToken);
                if (live.Account is not null)
                {
                    await _profiles.WriteProfileAsync(outgoingFolder, live.Account, cancellationToken);
                }

                await _pairs.MoveLiveToParkedAsync(outgoingFolder, cancellationToken);
                await _journal.WriteAsync(entry with { StepReached = SwitchStep.Parked }, cancellationToken);
            }

            await _pairs.MoveParkedToLiveAsync(plan.IncomingFolderPath, cancellationToken);
            await _journal.WriteAsync(entry with { StepReached = SwitchStep.Unparked }, cancellationToken);

            if (_options.PatchStateFile)
            {
                await _stateFile.PatchAccountBlockAsync(plan.IncomingAccount, cancellationToken);
                await _journal.WriteAsync(entry with { StepReached = SwitchStep.Patched }, cancellationToken);
            }

            await WriteLiveOwnerAsync(targetCredentials.Fingerprint, plan.Incoming, cancellationToken);
            await _journal.ClearAsync(cancellationToken);
        }

        Result<ClaudeAuthStatus, string> verification = await _authStatus.ReadAsync(null, cancellationToken);
        bool mismatch = verification.IsSuccess
            && verification.Value.Email is string reported
            && !string.Equals(reported.Trim(), plan.Incoming.Value, StringComparison.OrdinalIgnoreCase);
        LogSwitched(plan.Outgoing?.Value, plan.Incoming.Value, mismatch);
        return Result<SwitchOutcome, SwitchRefusal>.Success(new SwitchOutcome(plan.Incoming, plan.Outgoing, verification, mismatch, _timeProvider.GetUtcNow()));
    }

    private async Task<LiveAccountState> SnapshotLiveAsync(CancellationToken cancellationToken)
    {
        OAuthAccountBlock? account = await _stateFile.ReadAccountBlockAsync(cancellationToken);
        CredentialPair? pair = await _pairs.ReadLiveAsync(cancellationToken);
        return new LiveAccountState(
            _options.LiveConfigDirectory,
            _options.StateFilePath,
            account,
            pair is not null,
            pair?.Fingerprint,
            _pairs.FreshLockFileName(_secondaryLockGuardAge));
    }

    private async Task<IReadOnlyList<string>> QuarantineDuplicateLineagesAsync(CancellationToken cancellationToken)
    {
        List<(string Path, bool IsLive, RefreshTokenFingerprint Fingerprint)> holders = [];
        string livePath = Path.Combine(_options.LiveConfigDirectory, FileSystemCredentialPairStore.FileName);
        if (await _pairs.ReadLiveAsync(cancellationToken) is CredentialPair live)
        {
            holders.Add((livePath, true, live.Fingerprint));
        }

        if (Directory.Exists(_options.ProfilesRoot))
        {
            foreach (string folder in Directory.EnumerateDirectories(_options.ProfilesRoot))
            {
                if (await _pairs.ReadParkedAsync(folder, cancellationToken) is CredentialPair parked)
                {
                    holders.Add((Path.Combine(folder, FileSystemCredentialPairStore.FileName), false, parked.Fingerprint));
                }
            }
        }

        List<string> quarantined = [];
        foreach (IGrouping<RefreshTokenFingerprint, (string Path, bool IsLive, RefreshTokenFingerprint Fingerprint)> lineage in holders.GroupBy(static holder => holder.Fingerprint))
        {
            (string Path, bool IsLive, RefreshTokenFingerprint Fingerprint)[] copies = [.. lineage];
            if (copies.Length < 2)
            {
                continue;
            }

            // The live copy is what every open session uses, so it is the one that stays.
            (string Path, bool IsLive, RefreshTokenFingerprint Fingerprint) kept = copies.FirstOrDefault(static copy => copy.IsLive, copies[0]);
            foreach ((string path, _, _) in copies.Where(copy => !string.Equals(copy.Path, kept.Path, StringComparison.Ordinal)))
            {
                string stamp = _timeProvider.GetUtcNow().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
                string folderName = Path.GetFileName(Path.GetDirectoryName(path)!);
                string destinationDirectory = Path.Combine(_quarantineDirectory, stamp + "-" + folderName);
                Directory.CreateDirectory(destinationDirectory);
                string destination = Path.Combine(destinationDirectory, FileSystemCredentialPairStore.FileName);
                File.Move(path, destination);
                quarantined.Add(destination);
                LogQuarantined(path, destination, lineage.Key.Sha256Hex[..12]);
            }
        }

        return quarantined;
    }

    private async Task<(string Outcome, bool Blocks)> ReconcileJournalAsync(CancellationToken cancellationToken)
    {
        SwitchJournalEntry? entry = await _journal.ReadOpenAsync(cancellationToken);
        if (entry is null)
        {
            return ("no open journal", false);
        }

        RefreshTokenFingerprint? liveFingerprint = (await _pairs.ReadLiveAsync(cancellationToken))?.Fingerprint;
        string incomingFolder = entry.IncomingFolderPath;

        if (liveFingerprint == entry.IncomingFingerprint)
        {
            // The unpark happened; only the patch, the owner record, and the clear may be missing.
            if (_options.PatchStateFile)
            {
                OAuthAccountBlock? current = await _stateFile.ReadAccountBlockAsync(cancellationToken);
                if (current?.Email != entry.Incoming)
                {
                    OAuthAccountBlock? incomingAccount = await _profiles.ReadAccountAsync(incomingFolder, cancellationToken);
                    if (incomingAccount is null)
                    {
                        return ("the unpark of " + entry.Incoming.Value + " completed but its account block is missing from " + incomingFolder + ", so the state file was not patched", true);
                    }

                    await _stateFile.PatchAccountBlockAsync(incomingAccount, cancellationToken);
                }
            }

            await WriteLiveOwnerAsync(entry.IncomingFingerprint, entry.Incoming, cancellationToken);
            await _journal.ClearAsync(cancellationToken);
            LogReconciled("completed", entry.Incoming.Value);
            return ("completed the switch to " + entry.Incoming.Value, false);
        }

        bool outgoingParked = entry.OutgoingFolderPath is string outgoingFolder
            && (await _pairs.ReadParkedAsync(outgoingFolder, cancellationToken))?.Fingerprint == entry.OutgoingFingerprint;
        bool incomingStillParked = (await _pairs.ReadParkedAsync(incomingFolder, cancellationToken))?.Fingerprint == entry.IncomingFingerprint;

        if (liveFingerprint is null && outgoingParked && incomingStillParked && entry.OutgoingFolderPath is string parkedFolder)
        {
            // Parked but never unparked: put the outgoing pair back, which restores the pre-switch
            // state. The move runs inside Claude Code's refresh lock like every other credential
            // move, so a session refreshing its cached pair cannot replace the restored file.
            Result<IAsyncDisposable, string> held = await _pairs.AcquireRefreshLockAsync(_options.RefreshLockWaitBound, cancellationToken);
            if (held.IsFailure)
            {
                LogReconciled("deferred", entry.Outgoing?.Value ?? "(none)");
                return ("the switch to " + entry.Incoming.Value + " is parked but not unwound yet: " + held.Error + "; restart once the session's refresh has finished", true);
            }

            await using (held.Value)
            {
                await _pairs.MoveParkedToLiveAsync(parkedFolder, cancellationToken);
                await _journal.ClearAsync(cancellationToken);
            }

            LogReconciled("unwound", entry.Outgoing?.Value ?? "(none)");
            return ("unwound the switch; " + (entry.Outgoing?.Value ?? "the previous pair") + " is live again", false);
        }

        bool nothingMoved = (liveFingerprint == entry.OutgoingFingerprint || (liveFingerprint is null && entry.OutgoingFingerprint is null)) && incomingStillParked;
        if (nothingMoved)
        {
            await _journal.ClearAsync(cancellationToken);
            LogReconciled("cleared", entry.Incoming.Value);
            return ("cleared a journal written before any move", false);
        }

        LogReconciled("unresolved", entry.Incoming.Value);
        return ("the live pair matches neither side of the journaled switch to " + entry.Incoming.Value + "; resolve by hand under " + _options.AppDataDirectory, true);
    }

    private bool QuarantineHoldsFiles() =>
        Directory.Exists(_quarantineDirectory) && Directory.EnumerateFiles(_quarantineDirectory, "*", SearchOption.AllDirectories).Any();

    private async Task<AccountEmail?> ReadLiveOwnerAsync(RefreshTokenFingerprint? liveFingerprint, CancellationToken cancellationToken)
    {
        if (liveFingerprint is null || !File.Exists(_liveOwnerPath))
        {
            return null;
        }

        var record = JsonNode.Parse(await File.ReadAllBytesAsync(_liveOwnerPath, cancellationToken)) as JsonObject;
        string? fingerprint = record?["fingerprint"]?.GetValue<string>();
        string? email = record?["email"]?.GetValue<string>();
        return fingerprint is not null && email is not null && new RefreshTokenFingerprint(fingerprint) == liveFingerprint
            ? new AccountEmail(email)
            : null;
    }

    private Task WriteLiveOwnerAsync(RefreshTokenFingerprint fingerprint, AccountEmail owner, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_liveOwnerPath)!);
        return AtomicJsonFile.WriteAsync(_liveOwnerPath, new JsonObject { ["fingerprint"] = fingerprint.Sha256Hex, ["email"] = owner.Value }, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "switched {From} -> {To} (cli mismatch: {Mismatch})")]
    private partial void LogSwitched(string? from, string to, bool mismatch);

    [LoggerMessage(Level = LogLevel.Warning, Message = "switch to {Target} refused: {Refusal}")]
    private partial void LogRefused(string target, SwitchRefusal refusal);

    [LoggerMessage(Level = LogLevel.Warning, Message = "switch to {Target} refused: refresh lock not acquired ({Detail})")]
    private partial void LogLockRefused(string target, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "quarantined a duplicate credential lineage {Fingerprint}: {Source} -> {Destination}")]
    private partial void LogQuarantined(string source, string destination, string fingerprint);

    [LoggerMessage(Level = LogLevel.Information, Message = "journal reconciliation {Outcome} for {Incoming}")]
    private partial void LogReconciled(string outcome, string incoming);
}
