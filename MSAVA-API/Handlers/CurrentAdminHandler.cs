using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
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

        try
        {
            bool userIsCurrentAdmin = await _context.Users
                .AsNoTracking()
                .AnyAsync(user => user.Id == userId.Value && user.IsAdmin && !user.IsBanned);

            if (userIsCurrentAdmin)
                context.Succeed(requirement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate admin status for user {UserId}", userId);
        }
    }
}
