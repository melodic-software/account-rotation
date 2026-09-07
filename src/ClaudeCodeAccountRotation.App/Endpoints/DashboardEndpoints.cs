using ClaudeCodeAccountRotation.App.Dashboard;
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
    }
}
