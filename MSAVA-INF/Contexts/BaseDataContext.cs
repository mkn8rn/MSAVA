using Microsoft.EntityFrameworkCore;
using MSAVA_INF.Models;

namespace MSAVA_INF.Contexts;

public class BaseDataContext : DbContext
{
    public BaseDataContext(DbContextOptions<BaseDataContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserDB>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<SavedFileDataDB>()
            .HasOne(fileData => fileData.FileReference)
            .WithOne()
            .HasForeignKey<SavedFileDataDB>(fileData => fileData.FileReferenceId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SavedFileReferenceDB>()
            .HasOne(fileReference => fileReference.AccessGroup)
            .WithMany()
            .HasForeignKey(fileReference => fileReference.AccessGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SavedFileDataDB>()
            .HasOne(fileData => fileData.Creator)
            .WithMany()
            .HasForeignKey(fileData => fileData.OriginalCreator)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SavedFileDataDB>()
            .HasOne(fileData => fileData.LastModifiedBy)
            .WithMany()
            .HasForeignKey(fileData => fileData.LastModifiedById)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AccessGroupDB>()
            .HasOne(ag => ag.Owner)
            .WithMany()
            .HasForeignKey(ag => ag.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserDB>()
            .HasOne(u => u.InviteCode)
            .WithMany()
            .HasForeignKey(u => u.InviteCodeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<InviteCodeDB>()
            .HasOne(ic => ic.Owner)
            .WithMany()
            .HasForeignKey(ic => ic.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public DbSet<InviteCodeDB> InviteCodes => Set<InviteCodeDB>();
    public DbSet<UserDB> Users => Set<UserDB>();
    public DbSet<JwtDB> Jwts => Set<JwtDB>();

    public DbSet<SavedFileReferenceDB> FileRefs => Set<SavedFileReferenceDB>();
    public DbSet<SavedFileDataDB> FileData => Set<SavedFileDataDB>();

    public DbSet<AccessCodeDB> AccessCodes => Set<AccessCodeDB>();
    public DbSet<AccessGroupDB> AccessGroups => Set<AccessGroupDB>();

    public DbSet<AccessLogDB> AccessLogs => Set<AccessLogDB>();
    public DbSet<UserLogDB> UserLogs => Set<UserLogDB>();
    public DbSet<ErrorLogDB> ErrorLogs => Set<ErrorLogDB>();
    public DbSet<GroupLogDB> GroupLogs => Set<GroupLogDB>();
    public DbSet<InviteLogDB> InviteLogs => Set<InviteLogDB>();
}
