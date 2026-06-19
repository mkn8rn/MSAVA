using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
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
                IsBanned = true,
                IsWhitelisted = true
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
    public async Task GetFileStreamByPathAsync_RejectsNonWhitelistedSessionBeforeMetadataLookup()
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
                Username = "not-whitelisted",
                AccessGroups = [],
                IsAdmin = true,
                IsWhitelisted = false
            });

            Func<Task> act = () => service.GetFileStreamByPathAsync("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.txt");

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("Users must be whitelisted before downloading files.");
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
            context.FileData.Add(CreateFileData(fileReference));
            context.SaveChanges();

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var session = new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "active-user",
                AccessGroups = [],
                IsAdmin = false,
                IsWhitelisted = true
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
    public async Task GetPhysicalFileReturnDataByIdAsync_ReturnsNotFoundWhenFileDataIsMissing()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();
        var fileReference = CreateFileReference(publicDownload: true);
        string contentPath = FileContentUtils.GetFullPath(fileReference.FileHash, "txt");

        DeleteFileIfPresent(contentPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            File.WriteAllText(contentPath, "physical-missing-file-data");
            context.FileRefs.Add(fileReference);
            await context.SaveChangesAsync();

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "active-user",
                AccessGroups = [],
                IsAdmin = false,
                IsWhitelisted = true
            });

            Func<Task> act = () => service.GetPhysicalFileReturnDataByIdAsync(fileReference.Id);

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage($"File with id {fileReference.Id} not found.");
            File.Exists(contentPath).Should().BeTrue();
            context.AccessLogs.Should().BeEmpty();
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GetFileStreamByIdAsync_IncrementsDownloadCountAfterOpeningContent()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();
        var fileReference = CreateFileReference(publicDownload: true);
        var fileData = CreateFileData(fileReference, downloadCount: 2);
        string contentPath = FileContentUtils.GetFullPath(fileReference.FileHash, "txt");

        DeleteFileIfPresent(contentPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            File.WriteAllText(contentPath, "id-download-count");
            context.FileRefs.Add(fileReference);
            context.FileData.Add(fileData);
            await context.SaveChangesAsync();

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "active-user",
                AccessGroups = [],
                IsAdmin = false,
                IsWhitelisted = true
            });

            var result = await service.GetFileStreamByIdAsync(fileReference.Id);
            await result.FileStream.DisposeAsync();

            context.FileData.Single(fileData => fileData.FileReferenceId == fileReference.Id)
                .DownloadCount
                .Should()
                .Be(3);
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GetFileStreamByIdAsync_ReturnsNotFoundWhenFileDataIsMissing()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();
        var fileReference = CreateFileReference(publicDownload: true);
        string contentPath = FileContentUtils.GetFullPath(fileReference.FileHash, "txt");

        DeleteFileIfPresent(contentPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            File.WriteAllText(contentPath, "missing-file-data-count");
            context.FileRefs.Add(fileReference);
            await context.SaveChangesAsync();

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "active-user",
                AccessGroups = [],
                IsAdmin = false,
                IsWhitelisted = true
            });

            Func<Task> act = () => service.GetFileStreamByIdAsync(fileReference.Id);

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage($"File with id {fileReference.Id} not found.");
            File.Exists(contentPath).Should().BeTrue();
            context.AccessLogs.Should().BeEmpty();
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GetFileStreamByIdAsync_RejectsUnknownReferenceExtensionBeforeOpeningContent()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();
        var fileReference = CreateFileReference(publicDownload: true);
        var fileData = CreateFileData(fileReference, downloadCount: 6);
        fileReference.FileExtension = FileExtensionType.Unknown;

        try
        {
            context.FileRefs.Add(fileReference);
            context.FileData.Add(fileData);
            await context.SaveChangesAsync();

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "active-user",
                AccessGroups = [],
                IsAdmin = false,
                IsWhitelisted = true
            });

            Func<Task> act = () => service.GetFileStreamByIdAsync(fileReference.Id);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage($"Saved file reference {fileReference.Id} has unsupported file extension 'Unknown'.");
            context.FileData.Single(fileData => fileData.FileReferenceId == fileReference.Id)
                .DownloadCount
                .Should()
                .Be(6);
            context.AccessLogs.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GetFileStreamByPathAsync_DeniesUnauthorizedSqlReferenceBeforeCheckingPhysicalFileExists()
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
            var fileReference = new SavedFileReferenceDB
            {
                Id = Guid.NewGuid(),
                FileHash = fileHash,
                FileExtension = FileExtensionType._TXT,
                AccessGroupId = Guid.NewGuid(),
                PublicDownload = false
            };
            metadataStore.AddMetadata(new SavedFileMetaRecord
            {
                RefId = fileReference.Id,
                FileHash = fileHash,
                FileExtension = "txt",
                AccessGroupId = fileReference.AccessGroupId,
                PublicDownload = false,
                CreatedAt = DateTime.UnixEpoch
            });
            context.FileRefs.Add(fileReference);
            context.FileData.Add(CreateFileData(fileReference));
            await context.SaveChangesAsync();
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "active-user",
                AccessGroups = [],
                IsAdmin = false,
                IsWhitelisted = true
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
    public async Task GetFileStreamByPathAsync_IncrementsDownloadCountForAuthorizedSqlReference()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();
        var accessGroupId = Guid.NewGuid();
        byte[] fileHash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).Take(32).ToArray();
        string fileNameWithExtension = $"{Convert.ToHexString(fileHash).ToLowerInvariant()}.txt";
        string contentPath = FileContentUtils.GetFullPath(fileHash, "txt");
        var fileReference = new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = fileHash,
            FileExtension = FileExtensionType._TXT,
            AccessGroupId = accessGroupId,
            PublicDownload = false
        };

        DeleteFileIfPresent(contentPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            File.WriteAllText(contentPath, "path-download-count");

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            metadataStore.AddMetadata(new SavedFileMetaRecord
            {
                RefId = fileReference.Id,
                FileHash = fileHash,
                FileExtension = "txt",
                AccessGroupId = accessGroupId,
                PublicDownload = false,
                CreatedAt = DateTime.UnixEpoch
            });
            context.FileRefs.Add(fileReference);
            context.FileData.Add(CreateFileData(fileReference, downloadCount: 4));
            await context.SaveChangesAsync();
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "active-user",
                AccessGroups = [accessGroupId],
                IsAdmin = false,
                IsWhitelisted = true
            });

            var result = await service.GetFileStreamByPathAsync(fileNameWithExtension);
            await result.FileStream.DisposeAsync();

            context.FileData.Single(fileData => fileData.FileReferenceId == fileReference.Id)
                .DownloadCount
                .Should()
                .Be(5);
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GetFileStreamByPathAsync_DeniesWhenMetadataAccessGroupDiffersFromSqlReference()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();
        var sqlAccessGroupId = Guid.NewGuid();
        var metadataAccessGroupId = Guid.NewGuid();
        byte[] fileHash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).Take(32).ToArray();
        string fileNameWithExtension = $"{Convert.ToHexString(fileHash).ToLowerInvariant()}.txt";
        string contentPath = FileContentUtils.GetFullPath(fileHash, "txt");
        var fileReference = new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = fileHash,
            FileExtension = FileExtensionType._TXT,
            AccessGroupId = sqlAccessGroupId,
            PublicDownload = false
        };

        DeleteFileIfPresent(contentPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            File.WriteAllText(contentPath, "metadata-access-drift");

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            metadataStore.AddMetadata(new SavedFileMetaRecord
            {
                RefId = fileReference.Id,
                FileHash = fileHash,
                FileExtension = "txt",
                AccessGroupId = metadataAccessGroupId,
                PublicDownload = false,
                CreatedAt = DateTime.UnixEpoch
            });
            context.FileRefs.Add(fileReference);
            context.FileData.Add(CreateFileData(fileReference, downloadCount: 7));
            await context.SaveChangesAsync();
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "metadata-only-user",
                AccessGroups = [metadataAccessGroupId],
                IsAdmin = false,
                IsWhitelisted = true
            });

            Func<Task> act = () => service.GetFileStreamByPathAsync(fileNameWithExtension);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("User does not have permission to access this file.");
            context.FileData.Single(fileData => fileData.FileReferenceId == fileReference.Id)
                .DownloadCount
                .Should()
                .Be(7);
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GetFileStreamByPathAsync_AllowsSqlAuthorizedReferenceWithoutMetadata()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();
        var accessGroupId = Guid.NewGuid();
        byte[] fileHash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).Take(32).ToArray();
        string fileNameWithExtension = $"{Convert.ToHexString(fileHash).ToLowerInvariant()}.txt";
        string contentPath = FileContentUtils.GetFullPath(fileHash, "txt");
        var fileReference = new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = fileHash,
            FileExtension = FileExtensionType._TXT,
            AccessGroupId = accessGroupId,
            PublicDownload = false
        };

        DeleteFileIfPresent(contentPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            File.WriteAllText(contentPath, "sql-authorized-path-download");

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            context.FileRefs.Add(fileReference);
            context.FileData.Add(CreateFileData(fileReference));
            await context.SaveChangesAsync();
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "sql-authorized-user",
                AccessGroups = [accessGroupId],
                IsAdmin = false,
                IsWhitelisted = true
            });

            var result = await service.GetFileStreamByPathAsync(fileNameWithExtension);

            result.Id.Should().Be(fileReference.Id);
            result.FileName.Should().Be(Path.GetFileNameWithoutExtension(fileNameWithExtension));
            result.FileExtension.Should().Be("txt");
            using var fileStream = result.FileStream;
            using var reader = new StreamReader(fileStream);
            reader.ReadToEnd().Should().Be("sql-authorized-path-download");
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GetPhysicalFileReturnDataByPathAsync_UsesCanonicalContentPathForMixedCaseStoredFileName()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();
        byte[] fileHash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).Take(32).ToArray();
        string requestedFileName = $"{Convert.ToHexString(fileHash).ToUpperInvariant()}.TXT";
        string contentPath = FileContentUtils.GetFullPath(fileHash, "txt");
        var fileReference = new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = fileHash,
            FileExtension = FileExtensionType._TXT,
            AccessGroupId = Guid.NewGuid(),
            PublicDownload = true
        };

        DeleteFileIfPresent(contentPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            File.WriteAllText(contentPath, "canonical-physical-path");

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            context.FileRefs.Add(fileReference);
            context.FileData.Add(CreateFileData(fileReference));
            await context.SaveChangesAsync();
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "active-user",
                AccessGroups = [],
                IsAdmin = false,
                IsWhitelisted = true
            });

            var result = await service.GetPhysicalFileReturnDataByPathAsync(requestedFileName);

            result.FilePath.Should().Be(contentPath);
            result.FileName.Should().Be(Path.GetFileName(contentPath));
            result.ContentType.Should().Be("text/plain");
            context.FileData.Single(fileData => fileData.FileReferenceId == fileReference.Id)
                .DownloadCount
                .Should()
                .Be(1);
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GetFileStreamByPathAsync_ReturnsCanonicalMetadataForMixedCaseStoredFileName()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();
        byte[] fileHash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).Take(32).ToArray();
        string requestedFileName = $"{Convert.ToHexString(fileHash).ToUpperInvariant()}.TXT";
        string contentPath = FileContentUtils.GetFullPath(fileHash, "txt");
        var fileReference = new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = fileHash,
            FileExtension = FileExtensionType._TXT,
            AccessGroupId = Guid.NewGuid(),
            PublicDownload = true
        };

        DeleteFileIfPresent(contentPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            File.WriteAllText(contentPath, "canonical-stream-metadata");

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            context.FileRefs.Add(fileReference);
            context.FileData.Add(CreateFileData(fileReference));
            await context.SaveChangesAsync();
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "active-user",
                AccessGroups = [],
                IsAdmin = false,
                IsWhitelisted = true
            });

            var result = await service.GetFileStreamByPathAsync(requestedFileName);

            result.Id.Should().Be(fileReference.Id);
            result.FileName.Should().Be(Path.GetFileNameWithoutExtension(contentPath));
            result.FileExtension.Should().Be("txt");
            using var fileStream = result.FileStream;
            using var reader = new StreamReader(fileStream);
            reader.ReadToEnd().Should().Be("canonical-stream-metadata");
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GetFileStreamByPathAsync_ReturnsNotFoundWhenSqlReferenceHasNoFileData()
    {
        using var context = CreateContext();
        var metadataDirectory = CreateTempDirectory();
        var accessGroupId = Guid.NewGuid();
        byte[] fileHash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).Take(32).ToArray();
        string fileNameWithExtension = $"{Convert.ToHexString(fileHash).ToLowerInvariant()}.txt";
        string contentPath = FileContentUtils.GetFullPath(fileHash, "txt");
        var fileReference = new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = fileHash,
            FileExtension = FileExtensionType._TXT,
            AccessGroupId = accessGroupId,
            PublicDownload = false
        };

        DeleteFileIfPresent(contentPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            File.WriteAllText(contentPath, "missing-path-file-data-count");

            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            metadataStore.AddMetadata(new SavedFileMetaRecord
            {
                RefId = fileReference.Id,
                FileHash = fileHash,
                FileExtension = "txt",
                AccessGroupId = accessGroupId,
                PublicDownload = false,
                CreatedAt = DateTime.UnixEpoch
            });
            context.FileRefs.Add(fileReference);
            await context.SaveChangesAsync();
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "active-user",
                AccessGroups = [accessGroupId],
                IsAdmin = false,
                IsWhitelisted = true
            });

            Func<Task> act = () => service.GetFileStreamByPathAsync(fileNameWithExtension);

            var exception = await act.Should().ThrowAsync<FileNotFoundException>()
                .WithMessage("No file reference found for the requested stored file.");
            exception.Which.Message.Should().NotContain(fileNameWithExtension);
            File.Exists(contentPath).Should().BeTrue();
            context.AccessLogs.Should().BeEmpty();
        }
        finally
        {
            DeleteFileIfPresent(contentPath);
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GetFileStreamByPathAsync_DeniesInvalidHashShapeAsUnauthorizedAccess()
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
                Username = "active-user",
                AccessGroups = [],
                IsAdmin = false,
                IsWhitelisted = true
            });

            Func<Task> act = () => service.GetFileStreamByPathAsync("abc.txt");

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("User does not have permission to access this file.");
        }
        finally
        {
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
            var fileReference = new SavedFileReferenceDB
            {
                Id = Guid.NewGuid(),
                FileHash = fileHash,
                FileExtension = FileExtensionType._TXT,
                AccessGroupId = Guid.NewGuid(),
                PublicDownload = false
            };
            metadataStore.AddMetadata(new SavedFileMetaRecord
            {
                RefId = fileReference.Id,
                FileHash = fileHash,
                FileExtension = "txt",
                AccessGroupId = fileReference.AccessGroupId,
                PublicDownload = false,
                CreatedAt = DateTime.UnixEpoch
            });
            context.FileRefs.Add(fileReference);
            context.FileData.Add(CreateFileData(fileReference));
            await context.SaveChangesAsync();
            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = Guid.NewGuid(),
                Username = "admin-user",
                AccessGroups = [],
                IsAdmin = true,
                IsWhitelisted = true
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
            new FileManager(metadataStore, NullLogger<FileManager>.Instance),
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

    private static SavedFileDataDB CreateFileData(
        SavedFileReferenceDB fileReference,
        uint downloadCount = 0)
    {
        string fileExtension = FileExtensionUtils.GetFileExtension(fileReference);

        return new SavedFileDataDB
        {
            Id = Guid.NewGuid(),
            FileReference = fileReference,
            FileReferenceId = fileReference.Id,
            SizeInBytes = 1,
            Checksum = Convert.ToHexString(fileReference.FileHash),
            Name = "download-test",
            Description = "download test file",
            MimeType = "text/plain",
            FileExtension = fileExtension,
            Tags = [],
            Categories = [],
            Metadata = JsonDocument.Parse("{}"),
            PublicViewing = false,
            DownloadCount = downloadCount,
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

        public Task<UserDTO> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<UserDTO>> GetAllUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsSessionUserAdminAsync(CancellationToken cancellationToken = default) => Task.FromResult(_session.IsAdmin);

        public Task<UserDTO> GetSessionUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid> GetSessionUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(_session.UserId);

        public Task<UserDB> GetSessionUserDBAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SessionDTO> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
        {
            SessionClaimsCalls++;
            return Task.FromResult(_session);
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
