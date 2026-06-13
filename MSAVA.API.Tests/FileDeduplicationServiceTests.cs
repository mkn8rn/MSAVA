using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class FileDeduplicationServiceTests
{
    [Test]
    public async Task CheckAndGetReferenceAsync_ReturnsFailureForBannedSessionBeforeReferenceLookup()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-banned-session-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "banned",
                IsAdmin = false,
                IsBanned = true,
                IsWhitelisted = true,
                Roles = ["Whitelisted", "Banned"],
                Claims = [],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-1),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            var request = new HashCheckRequest
            {
                ContentHashHex = Convert.ToHexString(contentHash),
                FileExtension = "txt",
                AccessGroupId = Guid.NewGuid(),
                FileName = "banned-copy",
                PublicViewing = false,
                PublicDownload = false
            };

            var result = await service.CheckAndGetReferenceAsync(request);

            result.Error.Should().Be("Banned users cannot check file hashes.");
            result.FileExists.Should().BeFalse();
            result.ReferenceId.Should().BeNull();
            result.NewReferenceCreated.Should().BeFalse();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
            metadataStore.GetByFileHash(contentHash, "txt").Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CheckAndGetReferenceAsync_RejectsRequestedAccessGroupOutsideCurrentUserMembership()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-unauthorized-group-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));

            var sessionUser = CreateUser("session");
            var existingOwner = CreateUser("owner");
            var otherOwner = CreateUser("other-owner");
            var sessionGroup = CreateAccessGroup(sessionUser, "session");
            var existingGroup = CreateAccessGroup(existingOwner, "existing");
            var unauthorizedGroup = CreateAccessGroup(otherOwner, "unauthorized");
            var existingReference = CreateFileReference(contentHash, existingGroup.Id);
            var existingData = CreateFileData(existingReference, existingOwner.Id);
            var existingMetadata = CreateMetadata(existingReference, existingGroup.Id);

            context.Users.AddRange(sessionUser, existingOwner, otherOwner);
            context.AccessGroups.AddRange(sessionGroup, existingGroup, unauthorizedGroup);
            context.FileRefs.Add(existingReference);
            context.FileData.Add(existingData);
            await context.SaveChangesAsync();
            metadataStore.AddMetadata(existingMetadata);

            var service = CreateService(context, metadataStore, sessionUser.Id);
            var request = new HashCheckRequest
            {
                ContentHashHex = Convert.ToHexString(contentHash),
                FileExtension = "txt",
                AccessGroupId = unauthorizedGroup.Id,
                FileName = "unauthorized-copy",
                PublicViewing = false,
                PublicDownload = false
            };

            Func<Task> act = () => service.CheckAndGetReferenceAsync(request);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("User cannot create a file reference in the requested access group.");

            metadataStore.GetByFileHash(contentHash, "txt")
                .Should()
                .ContainSingle(record => record.RefId == existingReference.Id);
            metadataStore.GetByAccessGroup(unauthorizedGroup.Id).Should().BeEmpty();
            context.FileRefs.Count().Should().Be(1);
            context.FileData.Count().Should().Be(1);
            context.ChangeTracker.Entries<SavedFileReferenceDB>()
                .Should()
                .NotContain(entry => entry.State == EntityState.Added);
            context.ChangeTracker.Entries<SavedFileDataDB>()
                .Should()
                .NotContain(entry => entry.State == EntityState.Added);
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CheckAndGetReferenceAsync_RemovesNewMetadataAndPendingEntitiesWhenDatabaseSaveFails()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-rollback-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateThrowingContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));

            var sessionUser = CreateUser("session");
            var existingOwner = CreateUser("owner");
            var targetGroup = CreateAccessGroup(sessionUser, "target");
            var existingGroup = CreateAccessGroup(existingOwner, "existing");
            var existingReference = CreateFileReference(contentHash, existingGroup.Id);
            var existingData = CreateFileData(existingReference, existingOwner.Id);
            var existingMetadata = CreateMetadata(existingReference, existingGroup.Id);

            context.Users.AddRange(sessionUser, existingOwner);
            context.AccessGroups.AddRange(targetGroup, existingGroup);
            context.FileRefs.Add(existingReference);
            context.FileData.Add(existingData);
            context.SaveChanges();
            metadataStore.AddMetadata(existingMetadata);

            var service = CreateService(context, metadataStore, sessionUser.Id);
            var request = new HashCheckRequest
            {
                ContentHashHex = Convert.ToHexString(contentHash),
                FileExtension = "txt",
                AccessGroupId = targetGroup.Id,
                FileName = "deduplicated-copy",
                Description = "copy from existing content",
                Tags = ["copy"],
                Categories = ["tests"],
                PublicViewing = false,
                PublicDownload = false
            };

            Func<Task> act = () => service.CheckAndGetReferenceAsync(request);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Simulated database failure.");

            metadataStore.GetByFileHash(contentHash, "txt")
                .Should()
                .ContainSingle(record => record.RefId == existingReference.Id);
            metadataStore.GetByAccessGroup(targetGroup.Id).Should().BeEmpty();
            context.FileRefs.Count().Should().Be(1);
            context.FileData.Count().Should().Be(1);
            context.ChangeTracker.Entries<SavedFileReferenceDB>()
                .Should()
                .NotContain(entry => entry.State == EntityState.Added);
            context.ChangeTracker.Entries<SavedFileDataDB>()
                .Should()
                .NotContain(entry => entry.State == EntityState.Added);
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    private static FileDeduplicationService CreateService(
        BaseDataContext context,
        MetadataStore metadataStore,
        Guid sessionUserId)
    {
        return CreateService(context, metadataStore, new SessionDTO
        {
            LoggedIn = true,
            UserId = sessionUserId,
            Username = "session",
            IsAdmin = false,
            IsBanned = false,
            IsWhitelisted = true,
            Roles = ["Whitelisted"],
            Claims = [],
            AccessGroups = [],
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        });
    }

    private static FileDeduplicationService CreateService(
        BaseDataContext context,
        MetadataStore metadataStore,
        SessionDTO session)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items["SessionDTO"] = session;

        return new FileDeduplicationService(
            context,
            metadataStore,
            new HttpContextAccessor { HttpContext = httpContext },
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
            NullLogger<FileDeduplicationService>.Instance);
    }

    private static BaseDataContext CreateThrowingContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ThrowingDataContext(options);
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private static UserDB CreateUser(string username)
    {
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = [1],
            PasswordSalt = [2],
            IsAdmin = false,
            IsBanned = false,
            IsWhitelisted = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static AccessGroupDB CreateAccessGroup(UserDB owner, string name)
    {
        var accessGroup = new AccessGroupDB
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Owner = owner,
            CreatedAt = DateTime.UtcNow,
            Name = name,
            Users = [],
            SubGroups = []
        };

        owner.AccessGroups.Add(accessGroup);
        accessGroup.Users.Add(owner);

        return accessGroup;
    }

    private static SavedFileReferenceDB CreateFileReference(byte[] contentHash, Guid accessGroupId)
    {
        return new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = contentHash,
            FileExtension = FileExtensionType._TXT,
            PublicDownload = false,
            AccessGroupId = accessGroupId
        };
    }

    private static SavedFileDataDB CreateFileData(SavedFileReferenceDB reference, Guid creatorId)
    {
        return new SavedFileDataDB
        {
            Id = Guid.NewGuid(),
            FileReferenceId = reference.Id,
            FileReference = reference,
            SizeInBytes = 20,
            Checksum = Convert.ToHexString(reference.FileHash),
            Name = "existing",
            Description = "existing reference",
            MimeType = "text/plain",
            FileExtension = "txt",
            Tags = ["existing"],
            Categories = ["tests"],
            Metadata = JsonDocument.Parse("{}"),
            PublicViewing = false,
            DownloadCount = 0,
            SavedAt = DateTime.UtcNow,
            OriginalCreator = creatorId,
            LastModifiedAt = DateTime.UtcNow,
            LastModifiedById = creatorId
        };
    }

    private static SavedFileMetaRecord CreateMetadata(SavedFileReferenceDB reference, Guid accessGroupId)
    {
        return new SavedFileMetaRecord
        {
            RefId = reference.Id,
            FileHash = reference.FileHash,
            FileExtension = "txt",
            AccessGroupId = accessGroupId,
            PublicDownload = false,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "msava-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class ThrowingDataContext : BaseDataContext
    {
        public ThrowingDataContext(DbContextOptions<BaseDataContext> options) : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromException<int>(new InvalidOperationException("Simulated database failure."));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SavedFileDataDB>().Ignore(fileData => fileData.Metadata);
        }
    }

    private sealed class TestDataContext : BaseDataContext
    {
        public TestDataContext(DbContextOptions<BaseDataContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SavedFileDataDB>().Ignore(fileData => fileData.Metadata);
        }
    }
}
