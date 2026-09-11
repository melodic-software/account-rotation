using System.Text.Json.Nodes;
using ClaudeCodeAccountRotation.App.Adapters.FileSystem;
using ClaudeCodeAccountRotation.App.Dashboard;
using ClaudeCodeAccountRotation.App.Security;
using ClaudeCodeAccountRotation.Core;
using ClaudeCodeAccountRotation.Core.Accounts;
using ClaudeCodeAccountRotation.Core.Identity;
using ClaudeCodeAccountRotation.Core.Ports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClaudeCodeAccountRotation.App.Endpoints;

/// <summary>
/// Browser-assisted login: the three calls that get an account the machine has
/// never seen onto the machine without a terminal.
/// <para>
/// Start runs the CLI under the account's own folder, captures the sign-in URL
/// it prints, and opens that URL in the browser profile the roster maps to the
/// account, so ten accounts do not fight over one signed-in browser profile. A
/// browser that could not be opened is reported beside the URL rather than
/// failing the login: the operator can still paste the URL.
/// </para>
/// <para>
/// The code call carries the one-time code in the body and hands it to the
/// child's standard input. It is never echoed back, never logged, and never an
/// argument.
/// </para>
/// </summary>
internal static class LoginEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        RouteGroupBuilder mutations = routes.MapGroup("/api").AddEndpointFilter<SameOriginMutationFilter>();

        mutations.MapPost("/accounts/{email}/login", static async (
            string email,
            RosterFile rosterFile,
            ProfileFolderStore profiles,
            ClaudeStateFile stateFile,
            ILoginSessionRunner runner,
            IBrowserLauncher browsers,
            CancellationToken cancellationToken) =>
        {
            Result<AccountEmail, string> parsed = AccountEmail.Parse(email);
            if (parsed.IsFailure)
            {
                return Results.BadRequest(new { error = parsed.Error });
            }

            AccountEmail target = parsed.Value;
            RosterEntry? entry = (await rosterFile.ReadAsync(cancellationToken)).Find(target);
            if (entry is null)
            {
                return Refused("NotOnRoster", target.Value + " is not on the roster; add it first so its browser mapping is known.");
            }

            // A login into the parked folder of the account the machine is already
            // signed in as would leave that account holding two logins, which is the
            // one thing the single-holder rule exists to prevent.
            if ((await stateFile.ReadAccountBlockAsync(cancellationToken))?.Email == target)
            {
                return Refused("AccountIsLive", "That account is already logged in on this machine; switch away from it before logging it in again.");
            }

            ParkedProfile folder = await profiles.EnsureFolderAsync(target, cancellationToken);
            Result<LoginSession, string> started = await runner.StartAsync(target, folder.FolderPath, cancellationToken);
            if (started.IsFailure)
            {
                return Refused("LoginCouldNotStart", started.Error);
            }

            LoginSession session = started.Value;
            string? browserError = entry.Browser is BrowserFamily browser
                ? browsers.Launch(browser, entry.BrowserProfileDirectory, session.SignInUrl!).Match<string?>(static _ => null, static error => error)
                : "no browser is mapped to this account; open the sign-in URL yourself, or map one with Edit.";
            return Results.Ok(View(session, browserError));
        });

        mutations.MapPost("/login-sessions/{id}/code", static async (
            string id,
            JsonObject body,
            ILoginSessionRunner runner,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(body);
            LoginSessionId session = new(id);
            if (runner.Status(session) is null)
            {
                return Results.NotFound(new { error = "no login session with that id is running" });
            }

            string code = body["code"] is JsonValue value && value.TryGetValue(out string? text) ? text : string.Empty;
            Result<LoginSession, string> submitted = await runner.SubmitCodeAsync(session, code, cancellationToken);
            return submitted.IsFailure
                ? Refused("CodeRefused", submitted.Error)
                : Results.Ok(View(submitted.Value, browserError: null));
        });

        // Mapped on the bare routes, outside the same-origin mutation filter, on
        // purpose: this is a read, like /api/dashboard and /api/browser-profiles.
        // The filter exists to stop a cross-origin page from changing state
        // through a request the browser would send anyway; a cross-origin GET
        // cannot read this response at all without CORS, and what it carries
        // (the session state and the sign-in URL the page already showed) is
        // no more sensitive than the dashboard read beside it. Guarding reads
        // on the loopback surface is #7's per-instance token, for every read at
        // once; putting this one behind the mutation filter would only make the
        // page's own status poll carry a mutation header for a request that
        // mutates nothing.
        routes.MapGet("/api/login-sessions/{id}", static (string id, ILoginSessionRunner runner) =>
            runner.Status(new LoginSessionId(id)) is LoginSession session
                ? Results.Ok(View(session, browserError: null))
                : Results.NotFound(new { error = "no login session with that id is running" }));
    }

    private static LoginSessionView View(LoginSession session, string? browserError) => new(
        session.Id.Value,
        session.Email.Value,
        session.State.ToString(),
        session.Message,
        session.SignInUrl?.AbsoluteUri,
        browserError,
        session.ExpiresAt);

    private static IResult Refused(string refusal, string message) =>
        Results.Json(new SwitchRefusalView(refusal, message), statusCode: StatusCodes.Status409Conflict);
}
