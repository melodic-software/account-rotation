using System.Globalization;
using ClaudeCodeAccountRotation.Core.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace ClaudeCodeAccountRotation.App.Security;

/// <summary>
/// Every mutating route: a cross-site form post carries no custom header and a
/// cross-origin fetch carries a foreign Origin, so requiring the
/// <c>X-Claude-Code-Account-Rotation</c> header and an Origin that is this
/// instance's own (or absent) refuses both before the handler runs. No CORS is
/// configured anywhere.
/// <para>
/// The Origin is compared against the address this instance was configured to
/// listen on, never against the request's own Host header. Deriving the
/// expected origin from the request would compare two values the same request
/// supplies, so a rebound name arriving in both would match itself and this
/// half of the filter would defend nothing.
/// </para>
/// </summary>
internal sealed class SameOriginMutationFilter : IEndpointFilter
{
    public const string HeaderName = "X-Claude-Code-Account-Rotation";

    private readonly string[] _ownOrigins;

    public SameOriginMutationFilter(ClaudeCodeAccountRotationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        // Kestrel binds loopback only, and a browser writes the name the operator
        // typed, so all three spellings of that one address are this instance's own.
        string port = ":" + configuration.ListenPort.ToString(CultureInfo.InvariantCulture);
        _ownOrigins = ["http://localhost" + port, "http://127.0.0.1" + port, "http://[::1]" + port];
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        HttpRequest request = context.HttpContext.Request;

        if (!request.Headers.ContainsKey(HeaderName))
        {
            return Results.Json(new { error = "mutations require the " + HeaderName + " header" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.Headers.TryGetValue("Origin", out StringValues origin)
            && origin.Count > 0
            && !_ownOrigins.Contains(origin.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            return Results.Json(new { error = "cross-origin mutations are refused" }, statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}
