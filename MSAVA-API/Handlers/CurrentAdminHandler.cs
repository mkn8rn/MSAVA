using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MSAVA_API.Authorization;
using MSAVA_INF.Contexts;

namespace MSAVA_API.Handlers;

public class CurrentAdminRequirement : IAuthorizationRequirement { }

public class CurrentAdminHandler : AuthorizationHandler<CurrentAdminRequirement>
{
    private readonly BaseDataContext _context;
    private readonly ILogger<CurrentAdminHandler> _logger;

    public CurrentAdminHandler(BaseDataContext context, ILogger<CurrentAdminHandler> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CurrentAdminRequirement requirement)
    {
        Guid? userId = AuthorizationUser.GetAuthenticatedUserId(context.User);
        if (userId is null)
            return;

        var cancellationToken = AuthorizationRequest.GetCancellationToken(context);

        try
        {
            bool userIsCurrentAdmin = await _context.Users
                .AsNoTracking()
                .AnyAsync(
                    user => user.Id == userId.Value &&
                        user.IsAdmin &&
                        !user.IsBanned &&
                        user.IsWhitelisted,
                    cancellationToken);

            if (userIsCurrentAdmin)
                context.Succeed(requirement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (RecoverableLookupFailurePolicy.IsRecoverable(ex))
        {
            _logger.LogError(ex, "Failed to validate admin status for user {UserId}", userId);
            context.Fail(new AuthorizationFailureReason(this, "Failed to validate current admin status."));
        }
    }
}
