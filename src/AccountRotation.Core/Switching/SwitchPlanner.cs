using AccountRotation.Core.Identity;

namespace AccountRotation.Core.Switching;

/// <summary>
/// Decides whether a switch may proceed and what it moves. Pure: every guard
/// from the swap spike, in the spike's order, then the three the plan review
/// added. Nothing here touches the machine.
/// </summary>
public static class SwitchPlanner
{
    public static Result<SwitchPlan, SwitchRefusal> Plan(SwitchPlanningInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (SameDirectory(input.Target.FolderPath, input.Live.LiveConfigDirectory))
        {
            return Refuse(SwitchRefusal.TargetIsLiveDirectory);
        }

        if (!input.Target.HasCredentials || input.TargetCredentials is null)
        {
            return Refuse(SwitchRefusal.TargetHasNoCredentials);
        }

        if (input.Target.Account?.Email is not AccountEmail incoming)
        {
            return Refuse(SwitchRefusal.TargetHasNoAccountBlock);
        }

        AccountEmail? liveEmail = input.Live.Account?.Email;
        if (liveEmail == incoming)
        {
            return Refuse(SwitchRefusal.AlreadyOnTarget);
        }

        if (input.LiveCredentials is not null && input.LiveCredentials.Fingerprint == input.TargetCredentials.Fingerprint)
        {
            return Refuse(SwitchRefusal.SharesLiveRefreshToken);
        }

        if (input.Live.FreshLockFileName is not null)
        {
            return Refuse(SwitchRefusal.RefreshLockPresent);
        }

        if (input.TargetCredentials.LoginExpiresAt is DateTimeOffset loginExpiresAt && loginExpiresAt <= input.Now)
        {
            return Refuse(SwitchRefusal.TargetLoginExpired);
        }

        if (input.Policy.BlocksSwitching)
        {
            return Refuse(SwitchRefusal.SwitchingBlockedByManagedPolicy);
        }

        bool ownerMismatch = input.LiveFingerprintOwner is AccountEmail owner && liveEmail is AccountEmail named && owner != named;
        if (input.JournalOpen || ownerMismatch)
        {
            return Refuse(SwitchRefusal.LiveIdentityUnverified);
        }

        AccountEmail? outgoing = input.Live.HasCredentials ? liveEmail : null;
        string? outgoingFolderPath = outgoing is AccountEmail parkedAs
            ? Path.Combine(input.ProfilesRoot, ProfileFolderName.FromEmail(parkedAs))
            : null;

        return Result<SwitchPlan, SwitchRefusal>.Success(new SwitchPlan(
            outgoing,
            outgoingFolderPath,
            incoming,
            input.Target.FolderPath,
            input.Target.Account));
    }

    private static bool SameDirectory(string first, string second)
    {
        string left = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
        string right = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }

    private static Result<SwitchPlan, SwitchRefusal> Refuse(SwitchRefusal refusal) =>
        Result<SwitchPlan, SwitchRefusal>.Failure(refusal);
}
