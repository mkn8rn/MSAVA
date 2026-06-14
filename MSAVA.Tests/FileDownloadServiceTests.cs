using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Contexts;
using MSAVA_INF.Managers;
using MSAVA_INF.Models;
using MSAVA_INF.Utils;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class FileDownloadServiceTests
{
    [Test]
    public async Task GetPhysicalFileReturnDataByIdAsync_RejectsAnonymousSessionEvenForPublicDownloadFile()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();
        var fileReference = CreateFileReference(publicDownload: true);

        try
        {
            context.FileRefs.Add(fileReference);
            context.SaveChanges();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = false,
                UserId = Guid.Empty,
                Username = string.Empty,
                AccessGroups = [],
                IsAdmin = false
            });

            Func<Task> act = () => service.GetPhysicalFileReturnDataByIdAsync(fileReference.Id);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("Session user is required to download files.");
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GetFileStreamByPathAsync_RejectsBannedSessionBeforeMetadataLookup()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "banned",
                AccessGroups = [],
                IsAdmin = true,
                IsBanned = true
            });

            Func<Task> act = () => service.GetFileStreamByPathAsync("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.txt");

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("Banned users cannot download files.");
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GetPhysicalFileReturnDataByIdAsync_ReusesAuthorizedSessionForAccessLog()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();
        var fileReference = CreateFileReference(publicDownload: true);
        string contentPath = FileContentUtils.GetFullPath(fileReference.FileHash, "txt");

        DeleteFileIfPresent(contentPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            File.WriteAllText(contentPath, "download-session-reuse");
            context.FileRefs.Add(fileReference);
            context.SaveChanges();

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var session = new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "active-user",
                AccessGroups = [],
                IsAdmin = false
            };
            var service = CreateService(context, metadataStore, session, out var userSessionService);

            var result = await service.GetPhysicalFileReturnDataByIdAsync(fileReference.Id);

            result.FilePath.Should().Be(contentPath);
            userSessionService.SessionClaimsCalls.Should().Be(1);
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GetFileStreamByPathAsync_DeniesUnauthorizedMetadataBeforeCheckingPhysicalFileExists()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();
        byte[] fileHash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).Take(32).ToArray();
        string fileNameWithExtension = $"{Convert.ToHexString(fileHash).ToLowerInvariant()}.txt";
        string contentPath = FileContentUtils.GetFullPath(fileHash, "txt");

        DeleteFileIfPresent(contentPath);

        try
        {
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            metadataStore.AddMetadata(new SavedFileMetaRecord
            {
                RefId = Guid.NewGuid(),
                FileHash = fileHash,
                FileExtension = "txt",
                AccessGroupId = Guid.NewGuid(),
                PublicDownload = false
            });
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "active-user",
                AccessGroups = [],
                IsAdmin = false
            });

            Func<Task> act = () => service.GetFileStreamByPathAsync(fileNameWithExtension);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("User does not have permission to access this file.");
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GetFileStreamByPathAsync_AllowsAdminWithoutAccessGroup()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();
        byte[] fileHash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).Take(32).ToArray();
        string fileNameWithExtension = $"{Convert.ToHexString(fileHash).ToLowerInvariant()}.txt";
        string contentPath = FileContentUtils.GetFullPath(fileHash, "txt");

        DeleteFileIfPresent(contentPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            File.WriteAllText(contentPath, "admin-path-download");

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            metadataStore.AddMetadata(new SavedFileMetaRecord
            {
                RefId = Guid.NewGuid(),
                FileHash = fileHash,
                FileExtension = "txt",
                AccessGroupId = Guid.NewGuid(),
                PublicDownload = false
            });
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "admin-user",
                AccessGroups = [],
                IsAdmin = true
            });

            var result = await service.GetFileStreamByPathAsync(fileNameWithExtension);

            result.FileName.Should().Be(Path.GetFileNameWithoutExtension(fileNameWithExtension));
            result.FileExtension.Should().Be("txt");
            using var fileStream = result.FileStream;
            using var reader = new StreamReader(fileStream);
            reader.ReadToEnd().Should().Be("admin-path-download");
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    private static FileDownloadService CreateService(
        BaseDataContext context,
        MetadataStore metadataStore,
        SessionDTO session)
    {
        return CreateService(context, metadataStore, session, out _);
    }

    private static FileDownloadService CreateService(
        BaseDataContext context,
        MetadataStore metadataStore,
        SessionDTO session,
        out TestUserSessionService userSessionService)
    {
        userSessionService = new TestUserSessionService(session);

        return new FileDownloadService(
            context,
            userSessionService,
            new FileManager(metadataStore),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context));
    }

    private static SavedFileReferenceDB CreateFileReference(bool publicDownload)
    {
        return new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).Take(32).ToArray(),
            FileExtension = FileExtensionType._TXT,
            PublicDownload = publicDownload,
            AccessGroupId = Guid.NewGuid()
        };
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
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

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private sealed class TestUserSessionService : IUserSessionService
    {
        private readonly SessionDTO _session;

        public TestUserSessionService(SessionDTO session)
        {
            _session = session;
        }

        public int SessionClaimsCalls { get; private set; }

        public UserDTO GetUserById(Guid id) => throw new NotSupportedException();

        public List<UserDTO> GetAllUsers() => throw new NotSupportedException();

        public bool IsSessionUserAdmin() => _session.IsAdmin;

        public UserDTO GetSessionUser() => throw new NotSupportedException();

        public Guid GetSessionUserId() => _session.UserId;

        public UserDB GetSessionUserDB() => throw new NotSupportedException();

        public SessionDTO GetSessionClaims()
        {
            SessionClaimsCalls++;
            return _session;
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
