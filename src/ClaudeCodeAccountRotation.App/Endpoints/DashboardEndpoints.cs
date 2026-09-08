using ClaudeCodeAccountRotation.App.Dashboard;
using ClaudeCodeAccountRotation.Core.Accounts;
using ClaudeCodeAccountRotation.Core.Ports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClaudeCodeAccountRotation.App.Endpoints;

internal static class DashboardEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        routes.MapGet("/api/dashboard", static async (DashboardAssembler assembler, CancellationToken cancellationToken) =>
            Results.Ok(await assembler.AssembleAsync(cancellationToken)));

        // What the roster's profile picker is populated from. A read, so it takes
        // the read endpoints' shape: no mutation header and no same-origin filter,
        // both of which exist to stop a cross-site write, not a local read.
        routes.MapGet("/api/browser-profiles", static async (IBrowserProfileReader profiles, CancellationToken cancellationToken) =>
            Results.Ok((await profiles.ReadAsync(cancellationToken)).Select(View).ToList()));
    }

    private static BrowserProfileView View(BrowserProfile profile) => new(
        profile.Browser.ToString().ToLowerInvariant(),
        profile.Directory,
        profile.Name,
        profile.SignedInAs?.Value);
}
