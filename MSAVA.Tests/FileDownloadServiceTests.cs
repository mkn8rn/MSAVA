using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Contexts;
using MSAVA_INF.Managers;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class FileDownloadServiceTests
{
    [Test]
    public void GetPhysicalFileReturnDataById_RejectsAnonymousSessionEvenForPublicDownloadFile()
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

            var act = () => service.GetPhysicalFileReturnDataById(fileReference.Id);

            act.Should().Throw<UnauthorizedAccessException>()
                .WithMessage("Session user is required to download files.");
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public void GetFileStreamByPath_RejectsBannedSessionBeforeMetadataLookup()
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

            var act = () => service.GetFileStreamByPath("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.txt");

            act.Should().Throw<UnauthorizedAccessException>()
                .WithMessage("Banned users cannot download files.");
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    private static FileDownloadService CreateService(
        BaseDataContext context,
        MetadataStore metadataStore,
        SessionDTO session)
    {
        return new FileDownloadService(
            context,
            new TestUserSessionService(session),
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

    private sealed class TestUserSessionService : IUserSessionService
    {
        private readonly SessionDTO _session;

        public TestUserSessionService(SessionDTO session)
        {
            _session = session;
        }

        public UserDTO GetUserById(Guid id) => throw new NotSupportedException();

        public List<UserDTO> GetAllUsers() => throw new NotSupportedException();

        public bool IsSessionUserAdmin() => _session.IsAdmin;

        public UserDTO GetSessionUser() => throw new NotSupportedException();

        public Guid GetSessionUserId() => _session.UserId;

        public UserDB GetSessionUserDB() => throw new NotSupportedException();

        public SessionDTO GetSessionClaims() => _session;
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
