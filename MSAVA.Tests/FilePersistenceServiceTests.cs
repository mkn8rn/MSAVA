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
using MSAVA_BLL.Services.Interfaces;
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
    public async Task CreateFileFromStreamAsync_RejectsBannedSessionBeforeWritingContent()
    {
        var content = Encoding.UTF8.GetBytes($"banned-session-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (sessionUser, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, sessionUser.Id, isBanned: true);
            var dto = CreateStreamDto(content, accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromStreamAsync(dto);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("Banned users cannot create files.");

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
    public async Task CreateFileFromStreamAsync_RejectsDatabaseBannedUserBeforeWritingContent()
    {
        var content = Encoding.UTF8.GetBytes($"database-banned-user-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var sessionUser = CreateUser("session", isBanned: true);
            var accessGroup = CreateAccessGroup(sessionUser, "session-files");
            context.Users.Add(sessionUser);
            context.AccessGroups.Add(accessGroup);
            context.SaveChanges();
            var service = CreateService(context, metadataStore, sessionUser.Id, isBanned: false);
            var dto = CreateStreamDto(content, accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromStreamAsync(dto);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("Banned users cannot create files.");

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
    public async Task CreateFileFromStreamAsync_RejectsDatabaseNonWhitelistedUserBeforeWritingContent()
    {
        var content = Encoding.UTF8.GetBytes($"database-non-whitelisted-user-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var sessionUser = CreateUser("session", isWhitelisted: false);
            var accessGroup = CreateAccessGroup(sessionUser, "session-files");
            context.Users.Add(sessionUser);
            context.AccessGroups.Add(accessGroup);
            context.SaveChanges();
            var service = CreateService(context, metadataStore, sessionUser.Id);
            var dto = CreateStreamDto(content, accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromStreamAsync(dto);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("Users must be whitelisted before creating files.");

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
    public async Task CreateFileFromStreamAsync_RejectsMissingFileNameBeforeWritingContent()
    {
        var content = Encoding.UTF8.GetBytes($"missing-name-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (sessionUser, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, sessionUser.Id);
            var dto = CreateStreamDto(content, accessGroup.Id);
            dto.FileName = " ";

            Func<Task> act = () => service.CreateFileFromStreamAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("FileName must be provided.*");

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
    public async Task CreateFileFromStreamAsync_NormalizesMetadataBeforePersisting()
    {
        var content = Encoding.UTF8.GetBytes($"normalized-stream-metadata-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (sessionUser, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, sessionUser.Id);
            var dto = CreateStreamDto(content, accessGroup.Id);
            dto.FileName = "  normalized-name  ";
            dto.Description = "  normalized description  ";
            dto.Tags = ["  alpha  ", "beta"];
            dto.Categories = ["  reference  "];

            Guid fileRefId = await service.CreateFileFromStreamAsync(dto);

            var fileData = context.ChangeTracker.Entries<SavedFileDataDB>()
                .Select(entry => entry.Entity)
                .Single(fileData => fileData.FileReferenceId == fileRefId);
            fileData.Name.Should().Be("normalized-name");
            fileData.Description.Should().Be("normalized description");
            fileData.Tags.Should().Equal("alpha", "beta");
            fileData.Categories.Should().Equal("reference");
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromStreamAsync_RejectsOversizedFileNameBeforeWritingContent()
    {
        var content = Encoding.UTF8.GetBytes($"oversized-name-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (sessionUser, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, sessionUser.Id);
            var dto = CreateStreamDto(content, accessGroup.Id);
            dto.FileName = new string('n', FileMetadataPolicy.MaximumFileNameLength + 1);

            Func<Task> act = () => service.CreateFileFromStreamAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage($"FileName must be {FileMetadataPolicy.MaximumFileNameLength} characters or fewer.");

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
    public async Task CreateFileFromStreamAsync_RejectsBlankTagBeforeWritingContent()
    {
        var content = Encoding.UTF8.GetBytes($"blank-tag-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (sessionUser, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, sessionUser.Id);
            var dto = CreateStreamDto(content, accessGroup.Id);
            dto.Tags = [" "];

            Func<Task> act = () => service.CreateFileFromStreamAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("Tags values must be provided.");

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
    public async Task CreateFileFromStreamAsync_RejectsUnsupportedExtensionBeforeWritingContent()
    {
        var content = Encoding.UTF8.GetBytes($"unsupported-extension-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var unknownContentPath = FileContentUtils.GetFullPath(hash, "unknown");
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(unknownContentPath);

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (sessionUser, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, sessionUser.Id);
            var dto = CreateStreamDto(content, accessGroup.Id);
            dto.FileExtension = "exe";

            Func<Task> act = () => service.CreateFileFromStreamAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("FileExtension 'exe' is not supported.*");

            File.Exists(unknownContentPath).Should().BeFalse();
            metadataStore.Exists(hash, "unknown").Should().BeFalse();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteFileIfPresent(unknownContentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromStreamAsync_RejectsOversizeStreamBeforeRegisteringFile()
    {
        var content = Encoding.UTF8.GetBytes("12345");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (sessionUser, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, sessionUser.Id, maximumFileSizeBytes: 4);
            var dto = CreateStreamDto(content, accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromStreamAsync(dto);

            await act.Should().ThrowAsync<FileTooLargeException>()
                .WithMessage("File size 5 bytes exceeds the maximum allowed size of 4 bytes.");

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
    public async Task CreateFileFromStreamAsync_ExtractsMetadataFromTempFileForNonSeekableInput()
    {
        var content = Encoding.UTF8.GetBytes("metadata from non seekable stream");
        var hash = SHA256.HashData(content);
        var contentPath = FileContentUtils.GetFullPath(hash, "txt");
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(contentPath);

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (sessionUser, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, sessionUser.Id);
            await using var stream = new NonSeekableReadStream(content);
            var dto = new SaveFileFromStreamDTO
            {
                FileName = "metadata-test",
                FileExtension = "txt",
                Stream = stream,
                AccessGroupId = accessGroup.Id,
                Tags = [],
                Categories = [],
                Description = string.Empty,
                PublicViewing = false,
                PublicDownload = false
            };

            Guid fileRefId = await service.CreateFileFromStreamAsync(dto);

            var fileData = context.ChangeTracker.Entries<SavedFileDataDB>()
                .Select(entry => entry.Entity)
                .Single(fileData => fileData.FileReferenceId == fileRefId);
            fileData.Metadata.RootElement.GetProperty("Valid").GetBoolean().Should().BeTrue();
            fileData.Metadata.RootElement.GetProperty("Type").GetString().Should().Be("Text");
            fileData.Metadata.RootElement.GetProperty("WordCount").GetInt32().Should().Be(5);
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromTempFileAsync_NormalizesMetadataBeforePersisting()
    {
        var content = Encoding.UTF8.GetBytes($"normalized-temp-metadata-{Guid.NewGuid()}");
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
            var (sessionUser, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, sessionUser.Id);
            var dto = CreateFetchDto(tempFilePath, accessGroup.Id);
            dto.FileName = "  normalized-fetch-name  ";
            dto.Description = "  normalized fetch description  ";
            dto.Tags = ["  fetched  "];
            dto.Categories = ["  imports  ", "tests"];

            Guid fileRefId = await service.CreateFileFromTempFileAsync(dto);

            var fileData = context.ChangeTracker.Entries<SavedFileDataDB>()
                .Select(entry => entry.Entity)
                .Single(fileData => fileData.FileReferenceId == fileRefId);
            fileData.Name.Should().Be("normalized-fetch-name");
            fileData.Description.Should().Be("normalized fetch description");
            fileData.Tags.Should().Equal("fetched");
            fileData.Categories.Should().Equal("imports", "tests");
            File.Exists(tempFilePath).Should().BeFalse();
        }
        finally
        {
            DeleteFileIfPresent(tempFilePath);
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromTempFileAsync_RejectsOversizeTempFileBeforeHashing()
    {
        var content = Encoding.UTF8.GetBytes("12345");
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
            var (sessionUser, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, sessionUser.Id, maximumFileSizeBytes: 4);
            var dto = CreateFetchDto(tempFilePath, accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromTempFileAsync(dto);

            await act.Should().ThrowAsync<FileTooLargeException>()
                .WithMessage("File size 5 bytes exceeds the maximum allowed size of 4 bytes.");

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
    public async Task CreateFileFromTempFileAsync_RejectsUnsupportedExtensionAndDeletesTempFile()
    {
        var content = Encoding.UTF8.GetBytes($"unsupported-temp-extension-{Guid.NewGuid()}");
        var hash = SHA256.HashData(content);
        var unknownContentPath = FileContentUtils.GetFullPath(hash, "unknown");
        var tempFilePath = Path.GetTempFileName();
        var metadataDirectory = CreateTempDirectory();

        DeleteFileIfPresent(unknownContentPath);

        try
        {
            await File.WriteAllBytesAsync(tempFilePath, content);

            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (sessionUser, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, sessionUser.Id);
            var dto = CreateFetchDto(tempFilePath, accessGroup.Id);
            dto.FileExtension = "exe";

            Func<Task> act = () => service.CreateFileFromTempFileAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("FileExtension 'exe' is not supported.*");

            File.Exists(tempFilePath).Should().BeFalse();
            File.Exists(unknownContentPath).Should().BeFalse();
            metadataStore.Exists(hash, "unknown").Should().BeFalse();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteFileIfPresent(tempFilePath);
            DeleteFileIfPresent(unknownContentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromTempFileAsync_DeletesTempFileWhenDtoValidationFails()
    {
        var content = Encoding.UTF8.GetBytes($"invalid-fetch-dto-{Guid.NewGuid()}");
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
            var (sessionUser, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, sessionUser.Id);
            var dto = CreateFetchDto(tempFilePath, accessGroup.Id);
            dto.FileExtension = " ";

            Func<Task> act = () => service.CreateFileFromTempFileAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("FileExtension must be provided.*");

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
        Guid? sessionUserId = null,
        bool isBanned = false,
        long maximumFileSizeBytes = FileSizePolicy.MaximumFileSizeBytes)
    {
        var fileManager = new FileManager(metadataStore, NullLogger<FileManager>.Instance);
        var serviceLogger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        SessionDTO? session = null;

        if (sessionUserId is not null)
        {
            session = new SessionDTO
            {
                LoggedIn = true,
                UserId = sessionUserId.Value,
                Username = "session",
                IsAdmin = false,
                IsBanned = isBanned,
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
            new TestRequestSessionAccessor(session),
            serviceLogger,
            NullLogger<FilePersistenceService>.Instance,
            maximumFileSizeBytes);
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

    private static UserDB CreateUser(
        string username,
        bool isBanned = false,
        bool isWhitelisted = true)
    {
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = [1],
            PasswordSalt = [2],
            IsAdmin = false,
            IsBanned = isBanned,
            IsWhitelisted = isWhitelisted,
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

    private sealed class TestRequestSessionAccessor : IRequestSessionAccessor
    {
        private readonly SessionDTO? _session;

        public TestRequestSessionAccessor(SessionDTO? session)
        {
            _session = session;
        }

        public SessionDTO? GetSession() => _session;
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

    private sealed class NonSeekableReadStream : MemoryStream
    {
        public NonSeekableReadStream(byte[] buffer) : base(buffer)
        {
        }

        public override bool CanSeek => false;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin loc)
        {
            throw new NotSupportedException();
        }
    }
}
