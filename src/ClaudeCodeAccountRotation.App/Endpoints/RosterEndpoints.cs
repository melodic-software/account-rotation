using System.Text.Json.Nodes;
using ClaudeCodeAccountRotation.App.Adapters.FileSystem;
using ClaudeCodeAccountRotation.App.Dashboard;
using ClaudeCodeAccountRotation.App.Security;
using ClaudeCodeAccountRotation.Core;
using ClaudeCodeAccountRotation.Core.Accounts;
using ClaudeCodeAccountRotation.Core.Configuration;
using ClaudeCodeAccountRotation.Core.Identity;
using ClaudeCodeAccountRotation.Core.Ports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClaudeCodeAccountRotation.App.Endpoints;

/// <summary>
/// Add, edit, remove, and adopt-live: everything the operator needs to get the
/// other nine accounts onto this machine without editing a file by hand.
/// <para>
/// Only Max accounts join. The tier is read from the CLI under the account's
/// own folder and from the folder's account block; a Team or Enterprise seat is
/// refused with its reason, because that seat exposes no usage buckets and is
/// never rotated. An account with nothing to read yet joins as "needs login"
/// and is judged again once it has a login.
/// </para>
/// <para>
/// Removal revokes by default (<c>?logout=true</c>). Deleting a folder leaves
/// recoverable bytes holding a refresh token valid for the rest of its 28 days,
/// so the revocation is the removal, and a failed logout refuses the delete
/// rather than silently leaving a live token behind. <c>?logout=false</c> is
/// the operator's deliberate override: the folder goes and the response says
/// the token was not revoked. A folder that holds no pair skips the logout
/// entirely; there is nothing to revoke.
/// </para>
/// </summary>
internal static class RosterEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        RouteGroupBuilder mutations = routes.MapGroup("/api").AddEndpointFilter<SameOriginMutationFilter>();

        mutations.MapPost("/accounts", static async (
            JsonObject body,
            RosterFile rosterFile,
            ProfileFolderStore profiles,
            ClaudeStateFile stateFile,
            IClaudeCliAuthStatus cli,
            ClaudeCodeAccountRotationConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(body);
            Result<AccountEmail, string> parsed = AccountEmail.Parse(Text(body, "email") ?? string.Empty);
            if (parsed.IsFailure)
            {
                return Results.BadRequest(new { error = parsed.Error });
            }

            AccountEmail email = parsed.Value;
            if ((await rosterFile.ReadAsync(cancellationToken)).Find(email) is not null)
            {
                return Refused("AlreadyOnRoster", email.Value + " is already on the roster.");
            }

            Result<BrowserFamily?, string> browser = ParseBrowser(body, "browser");
            if (browser.IsFailure)
            {
                return Results.BadRequest(new { error = browser.Error });
            }

            (MaxTierVerdict verdict, string? reason) = await JudgeAsync(email, profiles, stateFile, cli, configuration, cancellationToken);
            if (verdict == MaxTierVerdict.Refused)
            {
                return Refused("NotAMaxAccount", reason!);
            }

            ParkedProfile folder = await profiles.EnsureFolderAsync(email, cancellationToken);
            RosterEntry entry = new(
                email,
                Text(body, "alias"),
                browser.Value,
                Text(body, "browserProfileDirectory"),
                Paused: false,
                Text(body, "notes"));
            await rosterFile.UpdateAsync(roster => roster.With(entry), cancellationToken);
            return Results.Ok(new AccountCardView(
                email.Value,
                IsLive: false,
                folder.HasCredentials,
                folder.FolderPath,
                Roster: DashboardAssembler.View(entry)));
        });

        mutations.MapPatch("/accounts/{email}", static async (
            string email,
            JsonObject body,
            RosterFile rosterFile,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(body);
            Result<AccountEmail, string> parsed = AccountEmail.Parse(email);
            if (parsed.IsFailure)
            {
                return Results.BadRequest(new { error = parsed.Error });
            }

            RosterEntry? existing = (await rosterFile.ReadAsync(cancellationToken)).Find(parsed.Value);
            if (existing is null)
            {
                return Results.NotFound(new { error = parsed.Value.Value + " is not on the roster." });
            }

            Result<BrowserFamily?, string> browser = ParseBrowser(body, "browser");
            if (browser.IsFailure)
            {
                return Results.BadRequest(new { error = browser.Error });
            }

            // Presence-based, key by key: a record of nullable fields cannot tell
            // "clear the alias" from "leave the alias alone", and an edit that
            // silently reset the fields it did not mention would be worse than
            // one that refused.
            RosterEntry updated = existing with
            {
                Alias = body.ContainsKey("alias") ? Text(body, "alias") : existing.Alias,
                Browser = body.ContainsKey("browser") ? browser.Value : existing.Browser,
                BrowserProfileDirectory = body.ContainsKey("browserProfileDirectory") ? Text(body, "browserProfileDirectory") : existing.BrowserProfileDirectory,
                Paused = Flag(body, "paused") ?? existing.Paused,
                Notes = body.ContainsKey("notes") ? Text(body, "notes") : existing.Notes,
            };
            await rosterFile.UpdateAsync(roster => roster.With(updated), cancellationToken);
            return Results.Ok(DashboardAssembler.View(updated));
        });

        mutations.MapDelete("/accounts/{email}", static async (
            string email,
            bool? logout,
            RosterFile rosterFile,
            ProfileFolderStore profiles,
            ClaudeStateFile stateFile,
            IClaudeCliLogout cli,
            CancellationToken cancellationToken) =>
        {
            Result<AccountEmail, string> parsed = AccountEmail.Parse(email);
            if (parsed.IsFailure)
            {
                return Results.BadRequest(new { error = parsed.Error });
            }

            AccountEmail target = parsed.Value;
            if ((await stateFile.ReadAccountBlockAsync(cancellationToken))?.Email == target)
            {
                return Refused("AccountIsLive", "That account is live; switch to another account first.");
            }

            string folder = profiles.FolderPathFor(target);
            bool hasPair = File.Exists(Path.Combine(folder, FileSystemCredentialPairStore.FileName));
            bool revoke = logout ?? true;
            bool revoked = false;
            if (hasPair && revoke)
            {
                Result<Unit, string> loggedOut = await cli.LogoutAsync(folder, cancellationToken);
                if (loggedOut.IsFailure)
                {
                    return Refused(
                        "LogoutFailed",
                        "That account's login could not be revoked, so the folder was kept: " + loggedOut.Error
                        + " Retry, or remove with logout=false to delete the folder without revoking.");
                }

                revoked = true;
            }

            if (Directory.Exists(folder))
            {
                // Registers the folder with the store's own discovery guard, which
                // refuses to delete a path it has never listed. A folder that has
                // never been logged in carries no identity and so is never listed.
                await profiles.EnsureFolderAsync(target, cancellationToken);
                await profiles.DeleteFolderAsync(folder, cancellationToken);
            }

            await rosterFile.UpdateAsync(roster => roster.Without(target), cancellationToken);
            return Results.Ok(new RemovalView(
                target.Value,
                revoked,
                hasPair && !revoked
                    ? "The folder was deleted without a logout; that refresh token stays valid until its login expires."
                    : null));
        });

        mutations.MapPost("/accounts/{email}/adopt-live", static async (
            string email,
            RosterFile rosterFile,
            ProfileFolderStore profiles,
            ClaudeStateFile stateFile,
            IClaudeCliAuthStatus cli,
            ClaudeCodeAccountRotationConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            Result<AccountEmail, string> parsed = AccountEmail.Parse(email);
            if (parsed.IsFailure)
            {
                return Results.BadRequest(new { error = parsed.Error });
            }

            OAuthAccountBlock? liveAccount = await stateFile.ReadAccountBlockAsync(cancellationToken);
            if (liveAccount?.Email is not AccountEmail live)
            {
                return Refused("NoLiveAccount", "The state file names no live account to adopt.");
            }

            if (live != parsed.Value)
            {
                return Refused("NotTheLiveAccount", "The live account is " + live.Value + ", not " + parsed.Value.Value + ".");
            }

            Result<ClaudeAuthStatus, string> status = await cli.ReadAsync(configuration.LiveConfigDirectory, cancellationToken);
            (MaxTierVerdict verdict, string? reason) = MaxTierAdmission.Evaluate(status.IsSuccess ? status.Value : null, liveAccount);
            if (verdict == MaxTierVerdict.Refused)
            {
                return Refused("NotAMaxAccount", reason!);
            }

            RosterEntry entry = (await rosterFile.ReadAsync(cancellationToken)).Find(live) ?? new RosterEntry(live);
            await rosterFile.UpdateAsync(roster => roster.With(entry), cancellationToken);
            return Results.Ok(new AccountCardView(
                live.Value,
                IsLive: true,
                HasCredentials: true,
                profiles.FolderPathFor(live),
                Roster: DashboardAssembler.View(entry)));
        });
    }

    /// <summary>
    /// The tier evidence for an account that is not yet on the roster. The CLI
    /// is only run where there is a login to read: under the live directory when
    /// the account is the live one, or under its own folder when that folder
    /// holds a pair. A brand-new account has neither, and spawning the CLI on a
    /// folder that does not exist would cost a process and leave residue to say
    /// nothing.
    /// </summary>
    private static async Task<(MaxTierVerdict Verdict, string? Reason)> JudgeAsync(
        AccountEmail email,
        ProfileFolderStore profiles,
        ClaudeStateFile stateFile,
        IClaudeCliAuthStatus cli,
        ClaudeCodeAccountRotationConfiguration configuration,
        CancellationToken cancellationToken)
    {
        OAuthAccountBlock? liveAccount = await stateFile.ReadAccountBlockAsync(cancellationToken);
        if (liveAccount?.Email == email)
        {
            Result<ClaudeAuthStatus, string> live = await cli.ReadAsync(configuration.LiveConfigDirectory, cancellationToken);
            return MaxTierAdmission.Evaluate(live.IsSuccess ? live.Value : null, liveAccount);
        }

        string folder = profiles.FolderPathFor(email);
        if (!Directory.Exists(folder))
        {
            return (MaxTierVerdict.Unknown, null);
        }

        OAuthAccountBlock? account = await profiles.ReadAccountAsync(folder, cancellationToken);
        if (!File.Exists(Path.Combine(folder, FileSystemCredentialPairStore.FileName)))
        {
            return MaxTierAdmission.Evaluate(null, account);
        }

        Result<ClaudeAuthStatus, string> parked = await cli.ReadAsync(folder, cancellationToken);
        return MaxTierAdmission.Evaluate(parked.IsSuccess ? parked.Value : null, account);
    }

    private static IResult Refused(string refusal, string message) =>
        Results.Json(new SwitchRefusalView(refusal, message), statusCode: StatusCodes.Status409Conflict);

    private static Result<BrowserFamily?, string> ParseBrowser(JsonObject body, string key)
    {
        if (Text(body, key) is not string value)
        {
            return Result<BrowserFamily?, string>.Success(null);
        }

        return Enum.TryParse(value, ignoreCase: true, out BrowserFamily browser)
            ? Result<BrowserFamily?, string>.Success(browser)
            : Result<BrowserFamily?, string>.Failure("browser must be one of " + string.Join(", ", Enum.GetNames<BrowserFamily>()).ToLowerInvariant());
    }

    private static string? Text(JsonObject body, string key) =>
        body[key] is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static bool? Flag(JsonObject body, string key) =>
        body[key] is JsonValue value && value.TryGetValue(out bool flag) ? flag : null;
}

/// <summary>What a removal did, including whether the refresh token was revoked.</summary>
internal sealed record RemovalView(string Removed, bool LoggedOut, string? Warning);
