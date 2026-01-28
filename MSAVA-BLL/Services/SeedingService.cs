using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MSAVA_INF.Models;
using MSAVA_BLL.Utils;
using MSAVA_INF.Environment;
using System;
using System.Linq;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Loggers;
using MSAVA_INF.Contexts;
using Microsoft.EntityFrameworkCore;

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

            UserDB? adminUser = _context.Users.AsNoTracking().FirstOrDefault(u => u.Username == adminUsername);
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
            return adminUser.Id;
        }
    }
}
