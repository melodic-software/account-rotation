using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace ClaudeCodeAccountRotation.App.Security;

/// <summary>
/// Every mutating route: a cross-site form post carries no custom header and a
/// cross-origin fetch carries a foreign Origin, so requiring the
/// <c>X-Claude-Code-Account-Rotation</c> header and a same-origin (or absent) Origin refuses
/// both before the handler runs. No CORS is configured anywhere.
/// </summary>
internal sealed class SameOriginMutationFilter : IEndpointFilter
{
    public const string HeaderName = "X-Claude-Code-Account-Rotation";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        HttpRequest request = context.HttpContext.Request;

        if (!request.Headers.ContainsKey(HeaderName))
        {
            return Results.Json(new { error = "mutations require the " + HeaderName + " header" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.Headers.TryGetValue("Origin", out StringValues origin) && origin.Count > 0)
        {
            string expected = request.Scheme + "://" + request.Host.Value;
            if (!string.Equals(origin.ToString(), expected, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new { error = "cross-origin mutations are refused" }, statusCode: StatusCodes.Status403Forbidden);
            }
        }

        return await next(context);
    }
}
