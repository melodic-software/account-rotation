using System.Text.Json.Nodes;
using ClaudeCodeAccountRotation.Core.Identity;
using ClaudeCodeAccountRotation.Core.Ports;
using ClaudeCodeAccountRotation.Core.Accounts;

namespace ClaudeCodeAccountRotation.Core.Tests.Accounts;

public sealed class MaxTierAdmissionTests
{
    private static ClaudeAuthStatus Status(string? subscriptionType, bool loggedIn = true) =>
        new(loggedIn, "a@example.com", "claude.ai", "Personal", subscriptionType, null);

    private static OAuthAccountBlock Account(string? tier)
    {
        JsonObject raw = new() { ["emailAddress"] = "a@example.com" };
        if (tier is not null)
        {
            raw["organizationRateLimitTier"] = tier;
        }

        return OAuthAccountBlock.FromJson(raw);
    }

    [Fact]
    public void AMaxSubscriptionIsAdmitted()
    {
        MaxTierAdmission.Evaluate(Status("max"), Account(null)).Verdict.ShouldBe(MaxTierVerdict.Admitted);
    }

    [Fact]
    public void AClaudeMaxRateLimitTierIsAdmittedWithoutTheCli()
    {
        MaxTierAdmission.Evaluate(null, Account("claude_max_20x")).Verdict.ShouldBe(MaxTierVerdict.Admitted);
    }

    [Theory]
    [InlineData("team")]
    [InlineData("enterprise")]
    public void ATeamOrEnterpriseSeatIsRefusedWithItsReason(string subscriptionType)
    {
        (MaxTierVerdict verdict, string? reason) = MaxTierAdmission.Evaluate(Status(subscriptionType), Account("default_claude_ai"));

        verdict.ShouldBe(MaxTierVerdict.Refused);
        reason.ShouldNotBeNull();
        reason.ShouldContain(subscriptionType);
        reason.ShouldContain("Max");
    }

    [Fact]
    public void AFolderWithNoLoginIsUnknownRatherThanRefused()
    {
        MaxTierAdmission.Evaluate(Status(null, loggedIn: false), null).Verdict.ShouldBe(MaxTierVerdict.Unknown);
        MaxTierAdmission.Evaluate(null, null).Verdict.ShouldBe(MaxTierVerdict.Unknown);
    }
}
