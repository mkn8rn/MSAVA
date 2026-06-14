using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class FileQueryServiceTests
{
    [Test]
    public async Task GetAllFileMetadataAsync_ReturnsCurrentGroupsAndPublicViewingForNonAdmin()
    {
        using var context = CreateContext();
        var sessionGroupId = Guid.NewGuid();
        var otherGroupId = Guid.NewGuid();
        var sessionFile = CreateFileData("session-private", sessionGroupId, publicViewing: false);
        var publicFile = CreateFileData("other-public", otherGroupId, publicViewing: true);
        var hiddenFile = CreateFileData("other-private", otherGroupId, publicViewing: false);
        context.FileRefs.AddRange(sessionFile.FileReference!, publicFile.FileReference!, hiddenFile.FileReference!);
        context.FileData.AddRange(sessionFile, publicFile, hiddenFile);
        await context.SaveChangesAsync();

        var session = new SessionDTO
        {
            LoggedIn = true,
            UserId = Guid.NewGuid(),
            Username = "session",
            AccessGroups = [sessionGroupId],
            IsAdmin = false
        };
        var service = new FileQueryService(context, new TestUserSessionService(session));

        var metadata = await service.GetAllFileMetadataAsync();
        var ids = await service.GetAllFileGuidsAsync();

        metadata.Select(file => file.Name).Should().BeEquivalentTo(["session-private", "other-public"]);
        ids.Should().BeEquivalentTo([sessionFile.Id, publicFile.Id]);
    }

    [Test]
    public async Task GetAllFileMetadataAsync_ReturnsAllFilesForAdmin()
    {
        using var context = CreateContext();
        var firstFile = CreateFileData("first", Guid.NewGuid(), publicViewing: false);
        var secondFile = CreateFileData("second", Guid.NewGuid(), publicViewing: false);
        var publicFile = CreateFileData("public", Guid.NewGuid(), publicViewing: true);
        context.FileRefs.AddRange(firstFile.FileReference!, secondFile.FileReference!, publicFile.FileReference!);
        context.FileData.AddRange(firstFile, secondFile, publicFile);
        await context.SaveChangesAsync();

        var session = new SessionDTO
        {
            LoggedIn = true,
            UserId = Guid.NewGuid(),
            Username = "admin",
            AccessGroups = [],
            IsAdmin = true
        };
        var service = new FileQueryService(context, new TestUserSessionService(session));

        var metadata = await service.GetAllFileMetadataAsync();
        var ids = await service.GetAllFileGuidsAsync();

        metadata.Select(file => file.Name).Should().BeEquivalentTo(["first", "second", "public"]);
        ids.Should().BeEquivalentTo([firstFile.Id, secondFile.Id, publicFile.Id]);
    }

    [Test]
    public async Task GetAllFileMetadataAsync_RejectsAnonymousSession()
    {
        using var context = CreateContext();
        var publicFile = CreateFileData("public", Guid.NewGuid(), publicViewing: true);
        context.FileRefs.Add(publicFile.FileReference!);
        context.FileData.Add(publicFile);
        await context.SaveChangesAsync();

        var service = new FileQueryService(
            context,
            new TestUserSessionService(new SessionDTO
            {
                LoggedIn = false,
                UserId = Guid.Empty,
                Username = string.Empty,
                AccessGroups = [],
                IsAdmin = false
            }));

        Func<Task> act = () => service.GetAllFileMetadataAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Session user is required to query files.");
    }

    [Test]
    public async Task GetAllFileMetadataAsync_RejectsBannedSession()
    {
        using var context = CreateContext();
        var publicFile = CreateFileData("public", Guid.NewGuid(), publicViewing: true);
        context.FileRefs.Add(publicFile.FileReference!);
        context.FileData.Add(publicFile);
        await context.SaveChangesAsync();

        var service = new FileQueryService(
            context,
            new TestUserSessionService(new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "banned",
                AccessGroups = [],
                IsAdmin = false,
                IsBanned = true
            }));

        Func<Task> act = () => service.GetAllFileMetadataAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Banned users cannot query files.");
    }

    private static SavedFileDataDB CreateFileData(string name, Guid accessGroupId, bool publicViewing)
    {
        var reference = new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).Take(32).ToArray(),
            FileExtension = FileExtensionType._TXT,
            PublicDownload = false,
            AccessGroupId = accessGroupId
        };

        return new SavedFileDataDB
        {
            Id = Guid.NewGuid(),
            FileReferenceId = reference.Id,
            FileReference = reference,
            SizeInBytes = 12,
            Checksum = Convert.ToHexString(reference.FileHash),
            Name = name,
            Description = $"{name} description",
            MimeType = "text/plain",
            FileExtension = "txt",
            Tags = [name],
            Categories = ["tests"],
            Metadata = JsonDocument.Parse("{}"),
            PublicViewing = publicViewing,
            DownloadCount = 0,
            SavedAt = DateTime.UtcNow,
            OriginalCreator = Guid.NewGuid(),
            LastModifiedAt = DateTime.UtcNow,
            LastModifiedById = Guid.NewGuid()
        };
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
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

    private sealed class TestUserSessionService : IUserSessionService
    {
        private readonly SessionDTO _session;

        public TestUserSessionService(SessionDTO session)
        {
            _session = session;
        }

        public Task<UserDTO> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<UserDTO>> GetAllUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsSessionUserAdminAsync(CancellationToken cancellationToken = default) => Task.FromResult(_session.IsAdmin);
        public Task<UserDTO> GetSessionUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid> GetSessionUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(_session.UserId);
        public Task<UserDB> GetSessionUserDBAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SessionDTO> GetSessionClaimsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_session);
    }
}
