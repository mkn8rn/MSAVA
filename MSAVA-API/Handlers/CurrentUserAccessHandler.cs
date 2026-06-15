using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MSAVA_INF.Contexts;

namespace MSAVA_API.Handlers;

public class CurrentUserAccessRequirement : IAuthorizationRequirement { }

public class CurrentUserAccessHandler : AuthorizationHandler<CurrentUserAccessRequirement>
{
    private readonly BaseDataContext _context;
    private readonly ILogger<CurrentUserAccessHandler> _logger;

    public CurrentUserAccessHandler(BaseDataContext context, ILogger<CurrentUserAccessHandler> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CurrentUserAccessRequirement requirement)
    {
        Guid? userId = AuthorizationUser.GetAuthenticatedUserId(context.User);
        if (userId is null)
            return;

        var cancellationToken = AuthorizationRequest.GetCancellationToken(context);

        try
        {
            bool userCanAccess = await _context.Users
                .AsNoTracking()
                .AnyAsync(
                    user => user.Id == userId.Value &&
                        !user.IsBanned &&
                        user.IsWhitelisted,
                    cancellationToken);

            if (userCanAccess)
                context.Succeed(requirement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate current access for user {UserId}", userId);
            context.Fail(new AuthorizationFailureReason(this, "Failed to validate current user access."));
        }
    }
}
