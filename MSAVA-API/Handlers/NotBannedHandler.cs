using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MSAVA_INF.Contexts;

namespace MSAVA_API.Handlers
{
    public class NotBannedRequirement : IAuthorizationRequirement { }

    public class NotBannedHandler : AuthorizationHandler<NotBannedRequirement>
    {
        private readonly BaseDataContext _context;
        private readonly ILogger<NotBannedHandler> _logger;

        public NotBannedHandler(BaseDataContext context, ILogger<NotBannedHandler> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, NotBannedRequirement requirement)
        {
            Guid? userId = AuthorizationUser.GetAuthenticatedUserId(context.User);
            if (userId is null)
                return;

            var cancellationToken = AuthorizationRequest.GetCancellationToken(context);

            try
            {
                bool userCanAccess = await _context.Users
                    .AsNoTracking()
                    .AnyAsync(user => user.Id == userId.Value && !user.IsBanned, cancellationToken);

                if (userCanAccess)
                    context.Succeed(requirement);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate ban status for user {UserId}", userId);
                context.Fail(new AuthorizationFailureReason(this, "Failed to validate ban status."));
            }
        }
    }
}
