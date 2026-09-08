using ClaudeCodeAccountRotation.App.Adapters.FileSystem;
using ClaudeCodeAccountRotation.App.Switching;
using ClaudeCodeAccountRotation.Core.Accounts;
using ClaudeCodeAccountRotation.Core.Identity;
using ClaudeCodeAccountRotation.Core.Ports;
using ClaudeCodeAccountRotation.Core.Quota;

namespace ClaudeCodeAccountRotation.App.Dashboard;

/// <summary>
/// Builds the page's model from three sources that only partly overlap: the
/// live directory, the profile folders on disk, and the roster the operator
/// edits. An account gets one card whichever of the three it appears in, so a
/// roster entry that has never been logged in still shows up (as "needs
/// login") and a folder that predates the roster still shows up (with Adopt
/// offered). Every read first repairs a state file a session wrote a stale
/// block back into, so the page never shows the outgoing account as live for
/// long. Ranking joins in a later phase.
/// </summary>
internal sealed class DashboardAssembler(
    ClaudeStateFile stateFile,
    ICredentialPairStore pairs,
    ProfileFolderStore profiles,
    RosterFile rosterFile,
    RateLimitGuardTeeFileReader tee,
    LiveDirectorySwitch executor,
    DashboardState state,
    TimeProvider timeProvider)
{
    /// <summary>The roster entry as the page reads it.</summary>
    public static RosterEntryView? View(RosterEntry? entry) => entry is null
        ? null
        : new RosterEntryView(
            entry.Email.Value,
            entry.Alias,
            entry.Browser?.ToString().ToLowerInvariant(),
            entry.BrowserProfileDirectory,
            entry.Paused,
            entry.Notes);

    public async Task<DashboardView> AssembleAsync(CancellationToken cancellationToken)
    {
        _ = await executor.RepairStaleIdentityAsync(cancellationToken);
        OAuthAccountBlock? liveAccount = await stateFile.ReadAccountBlockAsync(cancellationToken);
        CredentialPair? livePair = await pairs.ReadLiveAsync(cancellationToken);
        IReadOnlyList<ParkedProfile> parked = await profiles.ListAsync(cancellationToken);
        StatuslineSnapshot? snapshot = await tee.ReadAsync(cancellationToken);
        Roster roster = await rosterFile.ReadAsync(cancellationToken);
        AccountEmail? liveEmail = liveAccount?.Email;

        List<AccountCardView> cards = [];
        if (liveEmail is AccountEmail live)
        {
            ParkedProfile? ownFolder = parked.FirstOrDefault(profile => profile.Email == live);
            (StatuslineQuotaView? quota, string? note) = Attribute(snapshot, live);
            cards.Add(new AccountCardView(live.Value, IsLive: true, HasCredentials: livePair is not null, ownFolder?.FolderPath, quota, note, View(roster.Find(live))));
        }

        cards.AddRange(parked
            .Where(profile => profile.Email != liveEmail)
            .Select(profile => new AccountCardView(profile.Email.Value, IsLive: false, profile.HasCredentials, profile.FolderPath, Roster: View(roster.Find(profile.Email)))));

        // A roster entry the operator added but has not logged in yet owns no
        // identity file, so the folder listing above cannot see it. Its card is
        // what makes Add visible on the page at all.
        List<AccountCardView> rosterOnly = [.. roster.Entries
            .Where(entry => !cards.Exists(card => string.Equals(card.Email, entry.Email.Value, StringComparison.Ordinal)))
            .Select(entry => new AccountCardView(entry.Email.Value, IsLive: false, HasCredentials: false, profiles.FolderPathFor(entry.Email), Roster: View(entry)))];
        cards.AddRange(rosterOnly);
        cards.Sort(static (left, right) => string.CompareOrdinal(left.Email, right.Email));

        List<string> warnings = [];
        if (liveEmail is null && livePair is not null)
        {
            warnings.Add("The live directory holds a credential pair but the state file names no account.");
        }

        if (state.LastReconciliation is { SwitchingBlocked: true } blocked)
        {
            warnings.Add(blocked.JournalOutcome);
        }

        return new DashboardView(
            new LiveAccountView(liveEmail?.Value, livePair is not null, livePair?.Fingerprint.Sha256Hex[..12]),
            cards,
            state.LastReconciliation?.Banner,
            warnings,
            timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Decides whether the tee's observation belongs to <paramref name="account"/>.
    /// The tee file is last-writer-wins across every session on the machine, so a
    /// snapshot is only ever shown on the card it names, and there are four ways
    /// it names nothing usable:
    /// <list type="number">
    /// <item>no snapshot at all (the file is absent, unreadable, or carries no
    /// <c>rate_limits</c>): the card simply has no numbers;</item>
    /// <item>the snapshot carries no <c>account.email</c>, because the writer
    /// predates that field or could not attribute the observation;</item>
    /// <item>the snapshot names another account;</item>
    /// <item>the snapshot names this account but still carries the windows the
    /// outgoing account left behind, which is what a session mid-turn at switch
    /// time writes.</item>
    /// </list>
    /// </summary>
    private (StatuslineQuotaView? Quota, string? Note) Attribute(StatuslineSnapshot? snapshot, AccountEmail account)
    {
        if (snapshot is null)
        {
            return (null, null);
        }

        if (snapshot.Account is not AccountEmail observed)
        {
            return (null, "The statusline snapshot names no account, so it is not shown here.");
        }

        if (observed != account)
        {
            return (null, "The statusline snapshot names " + observed.Value + ", not this account.");
        }

        if (state.PreSwitchWindows is PreSwitchWindows before)
        {
            if (before.FiveHourResetsAt == snapshot.FiveHourResetsAt && before.SevenDayResetsAt == snapshot.SevenDayResetsAt)
            {
                return (null, "Pre-switch windows: this snapshot still carries the outgoing account's reset times.");
            }

            // The first snapshot whose windows differ is the incoming account's own.
            state.PreSwitchWindows = null;
        }

        return (
            new StatuslineQuotaView(
                snapshot.FiveHourPercent,
                snapshot.FiveHourResetsAt,
                snapshot.SevenDayPercent,
                snapshot.SevenDayResetsAt,
                snapshot.CapturedAt),
            null);
    }
}
