using MSAVA_BLL.Utils;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class SessionGuardTests
{
    [Test]
    public void RequireActive_ReturnsLoggedInNonBannedSession()
    {
        var session = new SessionDTO
        {
            LoggedIn = true,
            UserId = Guid.NewGuid(),
            Username = "active"
        };

        var result = SessionGuard.RequireActive(
            session,
            "missing",
            "banned");

        result.Should().BeSameAs(session);
    }

    [Test]
    public void RequireActive_RejectsMissingSessionWithCallerMessage()
    {
        Action act = () => SessionGuard.RequireActive(
            null,
            "session required",
            "banned");

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("session required");
    }

    [Test]
    public void TryRequireActive_RejectsEmptyUserIdWithoutThrowing()
    {
        var session = new SessionDTO
        {
            LoggedIn = true,
            UserId = Guid.Empty,
            Username = "empty-user"
        };

        var result = SessionGuard.TryRequireActive(
            session,
            out var activeSession,
            out var error,
            "session required",
            "banned");

        result.Should().BeFalse();
        activeSession.Should().BeNull();
        error.Should().Be("session required");
    }

    [Test]
    public void TryRequireActive_RejectsBannedSessionWithoutThrowing()
    {
        var session = new SessionDTO
        {
            LoggedIn = true,
            UserId = Guid.NewGuid(),
            Username = "banned",
            IsBanned = true
        };

        var result = SessionGuard.TryRequireActive(
            session,
            out var activeSession,
            out var error,
            "session required",
            "banned session");

        result.Should().BeFalse();
        activeSession.Should().BeNull();
        error.Should().Be("banned session");
    }
}
