using AccountRotation.App.Adapters.FileSystem;
using AccountRotation.Core.Identity;
using AccountRotation.Core.Ports;

namespace AccountRotation.App.Dashboard;

/// <summary>
/// Builds the page's model from the live directory and the profile folders:
/// one card per account, the live one first, plus the reconciliation banner.
/// Quota and ranking join in later phases.
/// </summary>
internal sealed class DashboardAssembler(
    ClaudeStateFile stateFile,
    ICredentialPairStore pairs,
    ProfileFolderStore profiles,
    DashboardState state,
    TimeProvider timeProvider)
{
    public async Task<DashboardView> AssembleAsync(CancellationToken cancellationToken)
    {
        OAuthAccountBlock? liveAccount = await stateFile.ReadAccountBlockAsync(cancellationToken);
        CredentialPair? livePair = await pairs.ReadLiveAsync(cancellationToken);
        IReadOnlyList<ParkedProfile> parked = await profiles.ListAsync(cancellationToken);
        AccountEmail? liveEmail = liveAccount?.Email;

        List<AccountCardView> cards = [];
        if (liveEmail is AccountEmail live)
        {
            ParkedProfile? ownFolder = parked.FirstOrDefault(profile => profile.Email == live);
            cards.Add(new AccountCardView(live.Value, IsLive: true, HasCredentials: livePair is not null, ownFolder?.FolderPath));
        }

        cards.AddRange(parked
            .Where(profile => profile.Email != liveEmail)
            .Select(profile => new AccountCardView(profile.Email.Value, IsLive: false, profile.HasCredentials, profile.FolderPath)));
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
}
