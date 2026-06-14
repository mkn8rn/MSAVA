using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace MSAVA_API.Handlers;

internal static class AuthorizationRequest
{
    public static CancellationToken GetCancellationToken(AuthorizationHandlerContext context)
    {
        return context.Resource is HttpContext httpContext
            ? httpContext.RequestAborted
            : CancellationToken.None;
    }
}
