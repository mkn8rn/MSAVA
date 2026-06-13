using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
using MSAVA_INF.Contexts;
using MSAVA_INF.Managers;
using MSAVA_INF.Models;
using MSAVA_INF.Utils;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class FilePersistenceServiceTests
{
    [Test]
    public async Task CreateFileFromStreamAsync_RemovesNewMetadataAndContentWhenDatabaseSaveFails()
    {
        var content = Encoding.UTF8.GetBytes($"rollback-new-content-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            using var context = CreateThrowingContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (sessionUser, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, sessionUser.Id);

            var dto = CreateStreamDto(content, accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromStreamAsync(dto);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Simulated database failure.");

            File.Exists(contentPath).Should().BeFalse();
            metadataStore.Exists(hash, "txt").Should().BeFalse();
            context.ChangeTracker.Entries<SavedFileReferenceDB>().Should().BeEmpty();
            context.ChangeTracker.Entries<SavedFileDataDB>().Should().BeEmpty();
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromStreamAsync_KeepsExistingContentAndMetadataWhenDatabaseSaveFails()
    {
        var content = Encoding.UTF8.GetBytes($"rollback-existing-content-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();
        var existingRecord = new SavedFileMetaRecord
        {
            RefId = Guid.NewGuid(),
            FileHash = hash,
            FileExtension = "txt",
            AccessGroupId = Guid.NewGuid(),
            PublicDownload = false,
            CreatedAt = DateTime.UtcNow
        };

        DeleteFileIfPresent(contentPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            await File.WriteAllBytesAsync(contentPath, content);

            using var context = CreateThrowingContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (sessionUser, accessGroup) = SeedUserWithAccessGroup(context);
            existingRecord.AccessGroupId = accessGroup.Id;
            metadataStore.AddMetadata(existingRecord);
            var service = CreateService(context, metadataStore, sessionUser.Id);

            var dto = CreateStreamDto(content, accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromStreamAsync(dto);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Simulated database failure.");

            File.Exists(contentPath).Should().BeTrue();
            metadataStore.GetByFileHash(hash, "txt")
                .Should()
                .ContainSingle(record => record.RefId == existingRecord.RefId);
            context.ChangeTracker.Entries<SavedFileReferenceDB>().Should().BeEmpty();
            context.ChangeTracker.Entries<SavedFileDataDB>().Should().BeEmpty();
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromStreamAsync_RejectsMissingSessionBeforeWritingContent()
    {
        var content = Encoding.UTF8.GetBytes($"missing-session-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (_, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore);
            var dto = CreateStreamDto(content, accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromStreamAsync(dto);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("User session not found.");

            File.Exists(contentPath).Should().BeFalse();
            metadataStore.Exists(hash, "txt").Should().BeFalse();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromStreamAsync_RejectsAccessGroupOutsideCurrentUserMembership()
    {
        var content = Encoding.UTF8.GetBytes($"unauthorized-group-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var sessionUser = CreateUser("session");
            var otherUser = CreateUser("other");
            var unauthorizedGroup = CreateAccessGroup(otherUser, "other-files");
            context.Users.AddRange(sessionUser, otherUser);
            context.AccessGroups.Add(unauthorizedGroup);
            context.SaveChanges();

            var service = CreateService(context, metadataStore, sessionUser.Id);
            var dto = CreateStreamDto(content, unauthorizedGroup.Id);

            Func<Task> act = () => service.CreateFileFromStreamAsync(dto);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("User cannot create a file in the requested access group.");

            File.Exists(contentPath).Should().BeFalse();
            metadataStore.Exists(hash, "txt").Should().BeFalse();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromTempFileAsync_DeletesTempFileWhenAccessGroupIsRejected()
    {
        var content = Encoding.UTF8.GetBytes($"unauthorized-temp-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var tempFilePath = Path.GetTempFileName();
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            await File.WriteAllBytesAsync(tempFilePath, content);

            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var sessionUser = CreateUser("session");
            var otherUser = CreateUser("other");
            var unauthorizedGroup = CreateAccessGroup(otherUser, "other-files");
            context.Users.AddRange(sessionUser, otherUser);
            context.AccessGroups.Add(unauthorizedGroup);
            context.SaveChanges();

            var service = CreateService(context, metadataStore, sessionUser.Id);
            var dto = CreateFetchDto(tempFilePath, unauthorizedGroup.Id);

            Func<Task> act = () => service.CreateFileFromTempFileAsync(dto);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("User cannot create a file in the requested access group.");

            File.Exists(tempFilePath).Should().BeFalse();
            File.Exists(contentPath).Should().BeFalse();
            metadataStore.Exists(hash, "txt").Should().BeFalse();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteFileIfPresent(tempFilePath);
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromTempFileAsync_DeletesTempFileWhenSessionIsMissing()
    {
        var content = Encoding.UTF8.GetBytes($"missing-session-temp-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var tempFilePath = Path.GetTempFileName();
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            await File.WriteAllBytesAsync(tempFilePath, content);

            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (_, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore);
            var dto = CreateFetchDto(tempFilePath, accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromTempFileAsync(dto);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("User session not found.");

            File.Exists(tempFilePath).Should().BeFalse();
            File.Exists(contentPath).Should().BeFalse();
            metadataStore.Exists(hash, "txt").Should().BeFalse();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteFileIfPresent(tempFilePath);
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    private static SaveFileFromStreamDTO CreateStreamDto(byte[] content, Guid accessGroupId)
    {
        return new SaveFileFromStreamDTO
        {
            FileName = "rollback-test",
            FileExtension = "txt",
            Stream = new MemoryStream(content),
            AccessGroupId = accessGroupId,
            Tags = [],
            Categories = [],
            Description = string.Empty,
            PublicViewing = false,
            PublicDownload = false
        };
    }

    private static SaveFileFromFetchDTO CreateFetchDto(string tempFilePath, Guid accessGroupId)
    {
        return new SaveFileFromFetchDTO
        {
            FileName = "rollback-test",
            FileExtension = "txt",
            TempFilePath = tempFilePath,
            AccessGroupId = accessGroupId,
            Tags = [],
            Categories = [],
            Description = string.Empty,
            PublicViewing = false,
            PublicDownload = false
        };
    }

    private static FilePersistenceService CreateService(
        BaseDataContext context,
        MetadataStore metadataStore,
        Guid? sessionUserId = null)
    {
        var fileManager = new FileManager(metadataStore);
        var serviceLogger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var httpContextAccessor = new TestHttpContextAccessor();

        if (sessionUserId is not null)
        {
            httpContextAccessor.HttpContext = new TestHttpContext();
            httpContextAccessor.HttpContext.Items["SessionDTO"] = new SessionDTO
            {
                LoggedIn = true,
                UserId = sessionUserId.Value,
                Username = "session",
                IsAdmin = false,
                IsBanned = false,
                IsWhitelisted = true,
                Roles = ["Whitelisted"],
                Claims = [],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-1),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
        }

        return new FilePersistenceService(
            context,
            fileManager,
            httpContextAccessor,
            serviceLogger,
            NullLogger<FilePersistenceService>.Instance);
    }

    private static (UserDB User, AccessGroupDB AccessGroup) SeedUserWithAccessGroup(BaseDataContext context)
    {
        var user = CreateUser("session");
        var accessGroup = CreateAccessGroup(user, "session-files");

        context.Users.Add(user);
        context.AccessGroups.Add(accessGroup);
        context.SaveChanges();

        return (user, accessGroup);
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

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private static BaseDataContext CreateThrowingContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ThrowingDataContext(options);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "msava-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class TestHttpContext : HttpContext
    {
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override HttpRequest Request => throw new NotSupportedException();
        public override HttpResponse Response => throw new NotSupportedException();
        public override ConnectionInfo Connection => throw new NotSupportedException();
        public override WebSocketManager WebSockets => throw new NotSupportedException();
        public override ClaimsPrincipal User { get; set; } = new();
        public override IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();
        public override IServiceProvider RequestServices { get; set; } = EmptyServiceProvider.Instance;
        public override CancellationToken RequestAborted { get; set; }
        public override string TraceIdentifier { get; set; } = Guid.NewGuid().ToString("N");
        public override ISession Session { get; set; } = null!;
#pragma warning disable CS0618
        [Obsolete]
        public override AuthenticationManager Authentication => throw new NotSupportedException();
#pragma warning restore CS0618

        public override void Abort()
        {
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
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
}
