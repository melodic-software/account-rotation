using ClaudeCodeAccountRotation.Core.Identity;
using ClaudeCodeAccountRotation.Core.Ports;

namespace ClaudeCodeAccountRotation.Core.Accounts;

/// <summary>Whether an account may join the rotation.</summary>
public enum MaxTierVerdict
{
    /// <summary>Nothing on disk says yet what this account is; it joins as "needs login".</summary>
    Unknown,

    /// <summary>A Max subscription: the only kind the rotation carries.</summary>
    Admitted,

    /// <summary>A Team or Enterprise seat. Never rotated.</summary>
    Refused,
}

/// <summary>
/// Reads the tier of an account from the two places that report it: the CLI's
/// own <c>claude auth status --json</c> run under the account's folder, and the
/// <c>organizationRateLimitTier</c> of the folder's account block. Only a Max
/// account is admitted; a Team or Enterprise seat is refused with its reason,
/// since that seat exposes no usage buckets and rotating it would move a
/// managed login the operator does not own.
/// </summary>
public static class MaxTierAdmission
{
    private const string MaxSubscriptionType = "max";
    private const string MaxRateLimitTier = "claude_max";

    /// <summary>
    /// Judges an account from whatever evidence exists. Both arguments may be
    /// null (a folder that has never been logged in), which is
    /// <see cref="MaxTierVerdict.Unknown"/>: the account joins the roster and
    /// the tier is judged again once it has a login to read.
    /// </summary>
    public static (MaxTierVerdict Verdict, string? Reason) Evaluate(ClaudeAuthStatus? status, OAuthAccountBlock? account)
    {
        if (account?.OrganizationRateLimitTier is string tier && tier.Contains(MaxRateLimitTier, StringComparison.OrdinalIgnoreCase))
        {
            return (MaxTierVerdict.Admitted, null);
        }

        if (status is not { LoggedIn: true, SubscriptionType: string subscription } || string.IsNullOrWhiteSpace(subscription))
        {
            return (MaxTierVerdict.Unknown, null);
        }

        return string.Equals(subscription, MaxSubscriptionType, StringComparison.OrdinalIgnoreCase)
            ? (MaxTierVerdict.Admitted, null)
            : (MaxTierVerdict.Refused, "that account reports a \"" + subscription + "\" subscription; only Max accounts are rotated, and a Team or Enterprise seat never is");
    }
}
