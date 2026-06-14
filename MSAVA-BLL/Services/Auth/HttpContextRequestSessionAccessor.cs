using Microsoft.AspNetCore.Http;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Auth;

public sealed class HttpContextRequestSessionAccessor : IRequestSessionAccessor
{
    public const string SessionItemKey = "SessionDTO";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextRequestSessionAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public SessionDTO? GetSession()
    {
        return _httpContextAccessor.HttpContext?.Items[SessionItemKey] as SessionDTO;
    }
}
