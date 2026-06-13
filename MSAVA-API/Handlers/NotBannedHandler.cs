using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MSAVA_INF.Contexts;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

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
            Guid? userId = GetAuthenticatedUserId(context.User);
            if (userId is null)
                return;

            try
            {
                bool userCanAccess = await _context.Users
                    .AsNoTracking()
                    .AnyAsync(user => user.Id == userId.Value && !user.IsBanned);

                if (userCanAccess)
                    context.Succeed(requirement);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate ban status for user {UserId}", userId);
            }
        }

        private static Guid? GetAuthenticatedUserId(ClaimsPrincipal user)
        {
            if (user.Identity?.IsAuthenticated != true)
                return null;

            string? userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);

            return Guid.TryParse(userIdClaim, out var userId) && userId != Guid.Empty
                ? userId
                : null;
        }
    }
}
