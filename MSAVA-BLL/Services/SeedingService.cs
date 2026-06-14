using MSAVA_INF.Models;
using MSAVA_BLL.Utils;
using MSAVA_INF.Environment;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Loggers;
using MSAVA_INF.Contexts;

namespace MSAVA_BLL.Services
{
    public class SeedingService : ISeedingService
    {
        private readonly BaseDataContext _context;
        private readonly ILocalEnvironment _env;
        private readonly ServiceLogger _serviceLogger;

        public SeedingService(
            BaseDataContext context,
            ILocalEnvironment env,
            ServiceLogger serviceLogger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
        }

        public void Seed()
        {
            SeedAdminUser();
        }

        private Guid SeedAdminUser()
        {
            string adminUsername = _env.Values.AdminUsername;
            string adminPassword = _env.Values.AdminPassword;

            UserDB? adminUser = _context.Users.FirstOrDefault(u => u.Username == adminUsername);
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
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(adminUser);
                _context.SaveChanges();
                _serviceLogger.WriteLog(UserLogAction.AccountCreation, $"Admin user '{adminUsername}' created during seeding.", adminUser.Id, null);
            }

            if (EnsureConfiguredAdminIsActive(adminUser))
                _context.SaveChanges();

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
    }
}
