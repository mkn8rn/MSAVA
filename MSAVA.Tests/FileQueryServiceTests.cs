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
    public void FileQueryPagePolicy_NormalizesValidPage()
    {
        var page = FileQueryPagePolicy.Normalize(skip: 10, take: 25);

        page.Should().Be(new FileQueryPage(10, 25));
    }

    [TestCase(-1, 1, "skip", "File query skip must be between 0 and 100000.")]
    [TestCase(100_001, 1, "skip", "File query skip must be between 0 and 100000.")]
    [TestCase(0, 0, "take", "File query take must be between 1 and 100.")]
    [TestCase(0, 101, "take", "File query take must be between 1 and 100.")]
    public void FileQueryPagePolicy_RejectsInvalidPageBounds(
        int skip,
        int take,
        string parameterName,
        string message)
    {
        Action act = () => FileQueryPagePolicy.Normalize(skip, take);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage($"{message}*")
            .And.ParamName.Should().Be(parameterName);
    }

    [Test]
    public void FileQuerySearchPolicy_UsesSharedMetadataLengthLimits()
    {
        FileQuerySearchPolicy.MaximumTagSearchLength.Should().Be(FileMetadataPolicy.MaximumMetadataValueLength);
        FileQuerySearchPolicy.MaximumCategorySearchLength.Should().Be(FileMetadataPolicy.MaximumMetadataValueLength);
        FileQuerySearchPolicy.MaximumNameSearchLength.Should().Be(FileMetadataPolicy.MaximumFileNameLength);
        FileQuerySearchPolicy.MaximumDescriptionSearchLength.Should().Be(FileMetadataPolicy.MaximumDescriptionLength);
    }

    [TestCase("literal", "literal")]
    [TestCase("100%", "100\\%")]
    [TestCase("file_name", "file\\_name")]
    [TestCase("C:\\Temp\\100%_done", "C:\\\\Temp\\\\100\\%\\_done")]
    public void BuildExactLikePattern_EscapesPostgresLikeWildcards(string value, string expectedPattern)
    {
        FileQueryService.BuildExactLikePattern(value).Should().Be(expectedPattern);
    }

    [Test]
    public void BuildContainsLikePattern_EscapesWildcardsInsideSubstringPattern()
    {
        string pattern = FileQueryService.BuildContainsLikePattern("  50%_done\\today  ");

        pattern.Should().Be("%50\\%\\_done\\\\today%");
    }

    [TestCase(null, null)]
    [TestCase("", null)]
    [TestCase(" ", null)]
    [TestCase("  visible  ", "visible")]
    public void NormalizeSearchText_TrimsSearchTextAndIgnoresBlankValues(
        string? value,
        string? expected)
    {
        string? normalized = FileQuerySearchPolicy.NormalizeSearchText(
            value,
            "value",
            "Value",
            maximumLength: 16);

        normalized.Should().Be(expected);
    }

    [TestCase("bad\nvalue")]
    [TestCase("bad\u0000value")]
    public void NormalizeSearchText_RejectsControlCharacters(string value)
    {
        Action act = () => FileQuerySearchPolicy.NormalizeSearchText(
            value,
            "value",
            "Value",
            maximumLength: 16);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Value search text contains invalid characters.*");
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void NormalizeSearchText_RejectsInvalidMaximumLength(int maximumLength)
    {
        Action act = () => FileQuerySearchPolicy.NormalizeSearchText(
            "value",
            "value",
            "Value",
            maximumLength);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("Search text maximum length must be greater than zero.*")
            .And.ParamName.Should().Be("maximumLength");
    }

    [TestCase("tag", FileQuerySearchPolicy.MaximumTagSearchLength, "Tag")]
    [TestCase("category", FileQuerySearchPolicy.MaximumCategorySearchLength, "Category")]
    [TestCase("name", FileQuerySearchPolicy.MaximumNameSearchLength, "Name")]
    [TestCase("description", FileQuerySearchPolicy.MaximumDescriptionSearchLength, "Description")]
    public async Task GetFileGuidsByAllFieldsAsync_RejectsOversizeSearchText(
        string fieldName,
        int maximumLength,
        string messageFieldName)
    {
        using var context = CreateContext();
        var session = new SessionDTO
        {
            LoggedIn = true,
            UserId = Guid.NewGuid(),
            Username = "session",
            AccessGroups = [],
            IsAdmin = true,
            IsWhitelisted = true
        };
        var service = new FileQueryService(context, new TestUserSessionService(session));
        string value = new('s', maximumLength + 1);

        Func<Task> act = () => service.GetFileGuidsByAllFieldsAsync(
            tag: fieldName == "tag" ? value : null,
            category: fieldName == "category" ? value : null,
            name: fieldName == "name" ? value : null,
            description: fieldName == "description" ? value : null);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"{messageFieldName} search text must be {maximumLength} characters or fewer.*");
    }

    [TestCase("tag", "Tag")]
    [TestCase("category", "Category")]
    [TestCase("name", "Name")]
    [TestCase("description", "Description")]
    public async Task GetFileGuidsByAllFieldsAsync_RejectsControlCharacterSearchText(
        string fieldName,
        string messageFieldName)
    {
        using var context = CreateContext();
        var session = new SessionDTO
        {
            LoggedIn = true,
            UserId = Guid.NewGuid(),
            Username = "session",
            AccessGroups = [],
            IsAdmin = true,
            IsWhitelisted = true
        };
        var service = new FileQueryService(context, new TestUserSessionService(session));
        const string value = "bad\nsearch";

        Func<Task> act = () => service.GetFileGuidsByAllFieldsAsync(
            tag: fieldName == "tag" ? value : null,
            category: fieldName == "category" ? value : null,
            name: fieldName == "name" ? value : null,
            description: fieldName == "description" ? value : null);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"{messageFieldName} search text contains invalid characters.*");
    }

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
            IsAdmin = false,
            IsWhitelisted = true
        };
        var service = new FileQueryService(context, new TestUserSessionService(session));

        var metadata = await service.GetAllFileMetadataAsync();
        var ids = await service.GetAllFileGuidsAsync();

        metadata.Select(file => file.Name).Should().BeEquivalentTo(["session-private", "other-public"]);
        ids.Should().BeEquivalentTo([sessionFile.FileReferenceId, publicFile.FileReferenceId]);
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
            IsAdmin = true,
            IsWhitelisted = true
        };
        var service = new FileQueryService(context, new TestUserSessionService(session));

        var metadata = await service.GetAllFileMetadataAsync();
        var ids = await service.GetAllFileGuidsAsync();

        metadata.Select(file => file.Name).Should().BeEquivalentTo(["first", "second", "public"]);
        ids.Should().BeEquivalentTo([firstFile.FileReferenceId, secondFile.FileReferenceId, publicFile.FileReferenceId]);
    }

    [Test]
    public async Task GetAllFileMetadataAsync_IgnoresRowsWithoutFileReference()
    {
        using var context = CreateContext();
        var accessGroupId = Guid.NewGuid();
        var visibleFile = CreateFileData("visible", accessGroupId, publicViewing: false);
        var orphanedFile = CreateFileData("orphaned", Guid.NewGuid(), publicViewing: true);
        orphanedFile.FileReference = null;
        context.FileRefs.Add(visibleFile.FileReference!);
        context.FileData.AddRange(visibleFile, orphanedFile);
        await context.SaveChangesAsync();

        var session = new SessionDTO
        {
            LoggedIn = true,
            UserId = Guid.NewGuid(),
            Username = "session",
            AccessGroups = [accessGroupId],
            IsAdmin = false,
            IsWhitelisted = true
        };
        var service = new FileQueryService(context, new TestUserSessionService(session));

        var metadata = await service.GetAllFileMetadataAsync();
        var ids = await service.GetAllFileGuidsAsync();

        metadata.Select(file => file.Name).Should().BeEquivalentTo(["visible"]);
        ids.Should().BeEquivalentTo([visibleFile.FileReferenceId]);
    }

    [Test]
    public async Task GetAllFileMetadataAsync_DefaultPageCapsResultsAndOrdersNewestFirst()
    {
        using var context = CreateContext();
        var accessGroupId = Guid.NewGuid();
        var start = DateTime.UtcNow.AddHours(-3);
        var files = Enumerable.Range(0, FileQueryPagePolicy.MaximumPageSize + 5)
            .Select(index => CreateFileData(
                $"file-{index:D3}",
                accessGroupId,
                publicViewing: false,
                savedAt: start.AddMinutes(index)))
            .ToList();
        context.FileRefs.AddRange(files.Select(file => file.FileReference!));
        context.FileData.AddRange(files);
        await context.SaveChangesAsync();

        var session = new SessionDTO
        {
            LoggedIn = true,
            UserId = Guid.NewGuid(),
            Username = "session",
            AccessGroups = [accessGroupId],
            IsAdmin = false,
            IsWhitelisted = true
        };
        var service = new FileQueryService(context, new TestUserSessionService(session));

        var metadata = await service.GetAllFileMetadataAsync();

        metadata.Should().HaveCount(FileQueryPagePolicy.MaximumPageSize);
        metadata.Select(file => file.Name)
            .Should()
            .Equal(files.OrderByDescending(file => file.SavedAt)
                .ThenBy(file => file.Id)
                .Take(FileQueryPagePolicy.MaximumPageSize)
                .Select(file => file.Name));
    }

    [Test]
    public async Task GetAllFileGuidsAsync_AppliesExplicitPage()
    {
        using var context = CreateContext();
        var accessGroupId = Guid.NewGuid();
        var start = DateTime.UtcNow.AddHours(-1);
        var files = Enumerable.Range(0, 5)
            .Select(index => CreateFileData(
                $"match-{index}",
                accessGroupId,
                publicViewing: false,
                savedAt: start.AddMinutes(index)))
            .ToList();
        context.FileRefs.AddRange(files.Select(file => file.FileReference!));
        context.FileData.AddRange(files);
        await context.SaveChangesAsync();

        var session = new SessionDTO
        {
            LoggedIn = true,
            UserId = Guid.NewGuid(),
            Username = "session",
            AccessGroups = [accessGroupId],
            IsAdmin = false,
            IsWhitelisted = true
        };
        var service = new FileQueryService(context, new TestUserSessionService(session));

        var ids = await service.GetAllFileGuidsAsync(skip: 1, take: 2);

        ids
            .Should()
            .Equal(files.OrderByDescending(file => file.SavedAt)
                .ThenBy(file => file.Id)
                .Skip(1)
                .Take(2)
                .Select(file => file.FileReferenceId));
    }

    [Test]
    public async Task GetAllFileMetadataAsync_RejectsPageSizeAboveMaximum()
    {
        using var context = CreateContext();
        var session = new SessionDTO
        {
            LoggedIn = true,
            UserId = Guid.NewGuid(),
            Username = "session",
            AccessGroups = [],
            IsAdmin = false,
            IsWhitelisted = true
        };
        var service = new FileQueryService(context, new TestUserSessionService(session));

        Func<Task> act = () => service.GetAllFileMetadataAsync(take: FileQueryPagePolicy.MaximumPageSize + 1);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage($"File query take must be between 1 and {FileQueryPagePolicy.MaximumPageSize}.*");
    }

    [Test]
    public async Task GetFileGuidsByAllFieldsAsync_ReturnsReferenceIdsForVisibleFiles()
    {
        using var context = CreateContext();
        var sessionGroupId = Guid.NewGuid();
        var sessionFile = CreateFileData("session-private", sessionGroupId, publicViewing: false);
        var publicFile = CreateFileData("other-public", Guid.NewGuid(), publicViewing: true);
        var hiddenFile = CreateFileData("other-private", Guid.NewGuid(), publicViewing: false);
        context.FileRefs.AddRange(sessionFile.FileReference!, publicFile.FileReference!, hiddenFile.FileReference!);
        context.FileData.AddRange(sessionFile, publicFile, hiddenFile);
        await context.SaveChangesAsync();

        var session = new SessionDTO
        {
            LoggedIn = true,
            UserId = Guid.NewGuid(),
            Username = "session",
            AccessGroups = [sessionGroupId],
            IsAdmin = false,
            IsWhitelisted = true
        };
        var service = new FileQueryService(context, new TestUserSessionService(session));

        var ids = await service.GetFileGuidsByAllFieldsAsync(null, null, null, null);

        ids.Should().BeEquivalentTo([sessionFile.FileReferenceId, publicFile.FileReferenceId]);
        ids.Should().NotContain(sessionFile.Id);
        ids.Should().NotContain(publicFile.Id);
        ids.Should().NotContain(hiddenFile.Id);
    }

    [Test]
    public async Task GetAllFileMetadataAsync_RejectsNonWhitelistedSession()
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
                Username = "not-whitelisted",
                AccessGroups = [],
                IsAdmin = true,
                IsWhitelisted = false
            }));

        Func<Task> act = () => service.GetAllFileMetadataAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Users must be whitelisted before querying files.");
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
                IsBanned = true,
                IsWhitelisted = true
            }));

        Func<Task> act = () => service.GetAllFileMetadataAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Banned users cannot query files.");
    }

    private static SavedFileDataDB CreateFileData(
        string name,
        Guid accessGroupId,
        bool publicViewing,
        DateTime? savedAt = null)
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
            SavedAt = savedAt ?? DateTime.UtcNow,
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
        public Task<SessionDTO> GetCurrentSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(_session);
    }
}
