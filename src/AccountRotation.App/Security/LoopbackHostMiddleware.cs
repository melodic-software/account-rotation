using Microsoft.AspNetCore.Http;

namespace AccountRotation.App.Security;

/// <summary>
/// Refuses any request whose Host header is not a loopback name. Kestrel only
/// binds loopback, so this catches a request forwarded or rebound from
/// elsewhere (DNS rebinding) before any handler runs.
/// </summary>
internal sealed class LoopbackHostMiddleware(RequestDelegate next)
{
    private static readonly string[] _loopbackHosts = ["localhost", "127.0.0.1", "[::1]", "::1"];

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string host = context.Request.Host.Host;
        if (!_loopbackHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return context.Response.WriteAsJsonAsync(new { error = "this page answers loopback requests only" });
        }

        return next(context);
    }
}
