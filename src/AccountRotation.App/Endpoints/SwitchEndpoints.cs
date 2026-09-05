using AccountRotation.App.Dashboard;
using AccountRotation.App.Security;
using AccountRotation.App.Switching;
using AccountRotation.Core;
using AccountRotation.Core.Identity;
using AccountRotation.Core.Switching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AccountRotation.App.Endpoints;

internal static class SwitchEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        RouteGroupBuilder mutations = routes.MapGroup("/api").AddEndpointFilter<SameOriginMutationFilter>();
        mutations.MapPost("/accounts/{email}/switch", static async (string email, LiveDirectorySwitch executor, CancellationToken cancellationToken) =>
        {
            Result<AccountEmail, string> target = AccountEmail.Parse(email);
            if (target.IsFailure)
            {
                return Results.BadRequest(new { error = target.Error });
            }

            Result<SwitchOutcome, SwitchRefusal> outcome = await executor.SwitchToAsync(target.Value, cancellationToken);
            return outcome.Match(
                static done => Results.Ok(new SwitchOutcomeView(
                    done.Now.Value,
                    done.ParkedAs?.Value,
                    done.CliVerification.IsSuccess ? done.CliVerification.Value.Email : null,
                    done.CliVerification.IsFailure ? done.CliVerification.Error : null,
                    done.IdentityMismatchWarning,
                    done.At)),
                static refusal => Results.Json(new SwitchRefusalView(refusal.ToString(), Describe(refusal)), statusCode: StatusCodes.Status409Conflict));
        });
    }

    private static string Describe(SwitchRefusal refusal) => refusal switch
    {
        SwitchRefusal.TargetIsLiveDirectory => "The target folder is the live config directory.",
        SwitchRefusal.TargetHasNoCredentials => "That account has no parked credentials; log in first.",
        SwitchRefusal.TargetHasNoAccountBlock => "That profile folder carries no account identity.",
        SwitchRefusal.AlreadyOnTarget => "That account is already live.",
        SwitchRefusal.SharesLiveRefreshToken => "That parked pair is the live pair's own lineage; a second holder is never created.",
        SwitchRefusal.RefreshLockPresent => "A session is refreshing its token right now; try again in a moment.",
        SwitchRefusal.TargetLoginExpired => "That account's login has expired; log in again.",
        SwitchRefusal.SwitchingBlockedByManagedPolicy => "A device-managed login policy pins this machine to one organization.",
        SwitchRefusal.LiveIdentityUnverified => "The live identity could not be verified; see the banner.",
        SwitchRefusal.MutationInProgress => "Another credential change is in progress.",
        _ => refusal.ToString(),
    };
}
