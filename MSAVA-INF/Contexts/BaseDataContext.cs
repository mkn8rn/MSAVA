using MSAVA_INF.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSAVA_INF.Contexts
{
    public class BaseDataContext : DbContext
    {
        public BaseDataContext(DbContextOptions<BaseDataContext> options) : base(options)
        {
            //todo
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Owner - AccessGroup one-to-many relationship
            modelBuilder.Entity<AccessGroupDB>()
                .HasOne(ag => ag.Owner)
                .WithMany()
                .HasForeignKey(ag => ag.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            // User - InviteCode many-to-one relationship
            modelBuilder.Entity<UserDB>()
                .HasOne(u => u.InviteCode)
                .WithMany()
                .HasForeignKey(u => u.InviteCodeId)
                .OnDelete(DeleteBehavior.Restrict);

            // InviteCode - Owner one-to-many relationship
            modelBuilder.Entity<InviteCodeDB>()
                .HasOne(ic => ic.Owner)
                .WithMany()
                .HasForeignKey(ic => ic.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        }

        // User entities
        public DbSet<InviteCodeDB> InviteCodes { get; set; }
        public DbSet<UserDB> Users { get; set; }
        public DbSet<JwtDB> Jwts { get; set; }

        // File entities
        public DbSet<SavedFileReferenceDB> FileRefs { get; set; }
        public DbSet<SavedFileDataDB> FileData { get; set; }

        // Access group entities
        public DbSet<AccessCodeDB> AccessCodes { get; set; }
        public DbSet<AccessGroupDB> AccessGroups { get; set; }

        // Log entities
        public DbSet<AccessLogDB> AccessLogs { get; set; }
        public DbSet<UserLogDB> UserLogs { get; set; }
        public DbSet<ErrorLogDB> ErrorLogs { get; set; }
        public DbSet<GroupLogDB> GroupLogs { get; set; }
        public DbSet<InviteLogDB> InviteLogs { get; set; }
    }
}
