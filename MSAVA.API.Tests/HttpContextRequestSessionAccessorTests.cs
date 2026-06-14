using Microsoft.AspNetCore.Http;
using MSAVA_BLL.Services.Auth;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class HttpContextRequestSessionAccessorTests
{
    [Test]
    public void GetSession_ReturnsSessionStoredUnderSharedKey()
    {
        var session = new SessionDTO
        {
            LoggedIn = true,
            UserId = Guid.NewGuid(),
            Username = "session-user",
            Roles = [],
            Claims = [],
            AccessGroups = [],
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        var httpContext = new DefaultHttpContext();
        httpContext.Items[HttpContextRequestSessionAccessor.SessionItemKey] = session;
        var accessor = new HttpContextRequestSessionAccessor(new HttpContextAccessor
        {
            HttpContext = httpContext
        });

        accessor.GetSession().Should().BeSameAs(session);
    }

    [Test]
    public void GetSession_ReturnsNullWhenHttpContextIsMissing()
    {
        var accessor = new HttpContextRequestSessionAccessor(new HttpContextAccessor());

        accessor.GetSession().Should().BeNull();
    }
}
