using MSAVA_INF.Models;
using MSAVA_BLL.Utils;
using MSAVA_INF.Environment;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Loggers;
using MSAVA_INF.Contexts;
using Microsoft.EntityFrameworkCore;
using MSAVA_BLL.Services.Auth;

namespace MSAVA_BLL.Services
{
    public class SeedingService : ISeedingService
    {
        private readonly BaseDataContext _context;
        private readonly ILocalEnvironment _env;
        private readonly ServiceLogger _serviceLogger;
        private readonly TimeProvider _timeProvider;

        public SeedingService(
            BaseDataContext context,
            ILocalEnvironment env,
            ServiceLogger serviceLogger,
            TimeProvider? timeProvider = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task SeedAsync(CancellationToken cancellationToken = default)
        {
            await SeedAdminUserAsync(cancellationToken);
        }

        private async Task<Guid> SeedAdminUserAsync(CancellationToken cancellationToken)
        {
            string adminUsername = AuthInputPolicy.NormalizeUsername(_env.Values.AdminUsername);
            string adminPassword = _env.Values.AdminPassword;
            AuthInputPolicy.EnsurePasswordAllowed(adminPassword);

            UserDB? adminUser = await _context.Users.SingleOrDefaultAsync(
                u => u.Username == adminUsername,
                cancellationToken);

            if (adminUser == null)
            {
                byte[] salt = PasswordUtils.GenerateSalt();
                byte[] hash = PasswordUtils.HashPassword(adminPassword, salt);
                adminUser = new UserDB
                {
                    Id = Guid.NewGuid(),
                    Username = adminUsername,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    IsAdmin = true,
                    IsBanned = false,
                    IsWhitelisted = true,
                    CreatedAt = GetUtcNow()
                };
                _context.Users.Add(adminUser);
                await _context.SaveChangesAsync(cancellationToken);
                await _serviceLogger.WriteLogAsync(UserLogAction.AccountCreation, $"Admin user '{adminUsername}' created during seeding.", adminUser.Id, null);
            }

            if (EnsureConfiguredAdminIsActive(adminUser))
                await _context.SaveChangesAsync(cancellationToken);

            return adminUser.Id;
        }

        private static bool EnsureConfiguredAdminIsActive(UserDB adminUser)
        {
            bool changed = false;

            if (!adminUser.IsAdmin)
            {
                adminUser.IsAdmin = true;
                changed = true;
            }

            if (adminUser.IsBanned)
            {
                adminUser.IsBanned = false;
                changed = true;
            }

            if (!adminUser.IsWhitelisted)
            {
                adminUser.IsWhitelisted = true;
                changed = true;
            }

            return changed;
        }

        private DateTime GetUtcNow()
        {
            return _timeProvider.GetUtcNow().UtcDateTime;
        }
    }
}
