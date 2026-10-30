namespace CertCoach.Api.Auth;

/// <summary>
/// Requires an <c>X-CSRF</c> header on state-changing requests. Browsers cannot add custom headers to
/// cross-site form posts, and a cross-origin fetch with one needs a CORS preflight this API never grants.
/// This backs up the SameSite=Lax session cookie.
/// </summary>
internal sealed class CsrfHeaderFilter : IEndpointFilter
{
    public const string HeaderName = "X-CSRF";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !request.Headers.ContainsKey(HeaderName))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing anti-forgery header.",
                detail: $"Send the {HeaderName} header with state-changing requests.");
        }

        return await next(context);
    }
}
