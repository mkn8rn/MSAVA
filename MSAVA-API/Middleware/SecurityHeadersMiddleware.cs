namespace MSAVA_API.Middleware;

internal sealed class SecurityHeadersMiddleware
{
    internal const string ContentTypeOptionsHeader = "X-Content-Type-Options";
    internal const string ContentTypeOptionsValue = "nosniff";
    internal const string FrameOptionsHeader = "X-Frame-Options";
    internal const string FrameOptionsValue = "DENY";
    internal const string ReferrerPolicyHeader = "Referrer-Policy";
    internal const string ReferrerPolicyValue = "no-referrer";
    internal const string PermissionsPolicyHeader = "Permissions-Policy";
    internal const string PermissionsPolicyValue = "camera=(), geolocation=(), microphone=()";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(static state =>
        {
            var httpContext = (HttpContext)state;
            IHeaderDictionary headers = httpContext.Response.Headers;

            headers[ContentTypeOptionsHeader] = ContentTypeOptionsValue;
            headers[FrameOptionsHeader] = FrameOptionsValue;
            headers[ReferrerPolicyHeader] = ReferrerPolicyValue;
            headers[PermissionsPolicyHeader] = PermissionsPolicyValue;

            return Task.CompletedTask;
        }, context);

        return _next(context);
    }
}
