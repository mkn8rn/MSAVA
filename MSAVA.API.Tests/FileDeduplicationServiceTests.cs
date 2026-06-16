using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Auth;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;
using MSAVA_INF.Utils;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class FileDeduplicationServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 16, 11, 45, 0, TimeSpan.Zero);
    private readonly List<string> _storedContentPaths = [];

    [TearDown]
    public void DeleteStoredContentFiles()
    {
        foreach (string path in _storedContentPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            DeleteFileIfPresent(path);
        }

        _storedContentPaths.Clear();
    }

    [Test]
    public async Task CheckAndGetReferenceAsync_ReturnsFailureForNullRequest()
    {
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, Guid.NewGuid());

            var result = await service.CheckAndGetReferenceAsync(null);

            result.Error.Should().Be("Hash check request is required.");
            result.FileExists.Should().BeFalse();
            result.ReferenceId.Should().BeNull();
            result.NewReferenceCreated.Should().BeFalse();
            result.ContentHashHex.Should().BeEmpty();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CheckAndGetReferenceAsync_ReturnsFailureForMissingFileExtension()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-missing-extension-{Guid.NewGuid()}"));
        var hashHex = Convert.ToHexString(contentHash);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, Guid.NewGuid());
            var request = new HashCheckRequest
            {
                ContentHashHex = hashHex,
                FileExtension = " . ",
                AccessGroupId = Guid.NewGuid(),
                FileName = "missing-extension-copy",
                PublicViewing = false,
                PublicDownload = false
            };

            var result = await service.CheckAndGetReferenceAsync(request);

            result.Error.Should().Be("FileExtension must be provided.");
            result.FileExists.Should().BeFalse();
            result.ReferenceId.Should().BeNull();
            result.NewReferenceCreated.Should().BeFalse();
            result.ContentHashHex.Should().Be(hashHex);
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
    public async Task CheckAndGetReferenceAsync_ReturnsFailureForUnsupportedFileExtension()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-unsupported-extension-{Guid.NewGuid()}"));
        var hashHex = Convert.ToHexString(contentHash);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, Guid.NewGuid());
            var request = new HashCheckRequest
            {
                ContentHashHex = hashHex,
                FileExtension = "exe",
                AccessGroupId = Guid.NewGuid(),
                FileName = "unsupported-extension-copy",
                PublicViewing = false,
                PublicDownload = false
            };

            var result = await service.CheckAndGetReferenceAsync(request);

            result.Error.Should().Be("FileExtension 'exe' is not supported.");
            result.FileExists.Should().BeFalse();
            result.ReferenceId.Should().BeNull();
            result.NewReferenceCreated.Should().BeFalse();
            result.ContentHashHex.Should().Be(hashHex);
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
            metadataStore.GetByFileHash(contentHash, "unknown").Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CheckAndGetReferenceBatchAsync_ReturnsFailureForNullRequestList()
    {
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, Guid.NewGuid());

            var results = await service.CheckAndGetReferenceBatchAsync(null);

            results.Should().ContainSingle();
            results[0].Error.Should().Be("Hash check batch request is required.");
            results[0].FileExists.Should().BeFalse();
            results[0].ReferenceId.Should().BeNull();
            results[0].NewReferenceCreated.Should().BeFalse();
            results[0].ContentHashHex.Should().BeEmpty();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CheckAndGetReferenceBatchAsync_ReturnsFailureForOversizedRequestList()
    {
        var metadataDirectory = CreateTempDirectory();
        var requests = Enumerable.Range(0, HashCheckBatchPolicy.MaximumRequestCount + 1)
            .Select(index => new HashCheckRequest
            {
                ContentHashHex = index.ToString("x64"),
                FileExtension = "txt",
                AccessGroupId = Guid.NewGuid(),
                FileName = $"oversized-batch-{index}",
                PublicViewing = false,
                PublicDownload = false
            })
            .ToList();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, Guid.NewGuid());

            var results = await service.CheckAndGetReferenceBatchAsync(requests);

            results.Should().ContainSingle();
            results[0].Error.Should().Be(HashCheckBatchPolicy.MaximumRequestCountMessage);
            results[0].FileExists.Should().BeFalse();
            results[0].ReferenceId.Should().BeNull();
            results[0].NewReferenceCreated.Should().BeFalse();
            results[0].ContentHashHex.Should().BeEmpty();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CheckAndGetReferenceAsync_ReturnsFailureForDatabaseBannedUserBeforeReferenceLookup()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-banned-session-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));

            var sessionUser = CreateUser("banned", isBanned: true);
            context.Users.Add(sessionUser);
            await context.SaveChangesAsync();

            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = sessionUser.Id,
                Username = sessionUser.Username,
                IsAdmin = false,
                IsBanned = false,
                IsWhitelisted = true,
                Roles = ["Whitelisted"],
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
    public async Task CheckAndGetReferenceAsync_ReturnsFailureForDatabaseNonWhitelistedUser()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-non-whitelisted-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));

            var sessionUser = CreateUser("session", isWhitelisted: false);
            var sessionGroup = CreateAccessGroup(sessionUser, "session");
            context.Users.Add(sessionUser);
            context.AccessGroups.Add(sessionGroup);
            await context.SaveChangesAsync();

            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = sessionUser.Id,
                Username = sessionUser.Username,
                IsAdmin = false,
                IsBanned = false,
                IsWhitelisted = true,
                Roles = ["Whitelisted"],
                Claims = [],
                AccessGroups = [sessionGroup.Id],
                IssuedAt = DateTime.UtcNow.AddMinutes(-1),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            var request = new HashCheckRequest
            {
                ContentHashHex = Convert.ToHexString(contentHash),
                FileExtension = "txt",
                AccessGroupId = sessionGroup.Id,
                FileName = "non-whitelisted-copy",
                PublicViewing = false,
                PublicDownload = false
            };

            var result = await service.CheckAndGetReferenceAsync(request);

            result.Error.Should().Be("Users must be whitelisted before checking file hashes.");
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
    public async Task CheckAndGetReferenceAsync_ReturnsExistingPrivateReferenceForAdminWithoutAccessGroup()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-admin-existing-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));

            var admin = CreateUser("admin", isAdmin: true);
            var existingOwner = CreateUser("owner");
            var existingGroup = CreateAccessGroup(existingOwner, "existing");
            var existingReference = CreateFileReference(contentHash, existingGroup.Id);

            context.Users.AddRange(admin, existingOwner);
            context.AccessGroups.Add(existingGroup);
            context.FileRefs.Add(existingReference);
            await context.SaveChangesAsync();

            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = admin.Id,
                Username = admin.Username,
                IsAdmin = true,
                IsBanned = false,
                IsWhitelisted = true,
                Roles = ["Admin", "Whitelisted"],
                Claims = [],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-1),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            var request = new HashCheckRequest
            {
                ContentHashHex = Convert.ToHexString(contentHash),
                FileExtension = "txt",
                FileName = "admin-copy",
                PublicViewing = false,
                PublicDownload = false
            };

            var result = await service.CheckAndGetReferenceAsync(request);

            result.FileExists.Should().BeTrue();
            result.ReferenceId.Should().Be(existingReference.Id);
            result.NewReferenceCreated.Should().BeFalse();
            result.Error.Should().BeNull();
            context.FileRefs.Should().ContainSingle(reference => reference.Id == existingReference.Id);
            metadataStore.GetByAccessGroup(existingGroup.Id).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CheckAndGetReferenceAsync_ReturnsNotFoundWhenSqlReferenceExistsButContentFileIsMissing()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-missing-content-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));

            var sessionUser = CreateUser("session");
            var sessionGroup = CreateAccessGroup(sessionUser, "session");
            var staleReference = CreateFileReference(contentHash, sessionGroup.Id, createStoredContent: false);

            context.Users.Add(sessionUser);
            context.AccessGroups.Add(sessionGroup);
            context.FileRefs.Add(staleReference);
            await context.SaveChangesAsync();

            var service = CreateService(context, metadataStore, sessionUser.Id);
            var request = new HashCheckRequest
            {
                ContentHashHex = Convert.ToHexString(contentHash),
                FileExtension = "txt",
                FileName = "missing-content-copy",
                PublicViewing = false,
                PublicDownload = false
            };

            var result = await service.CheckAndGetReferenceAsync(request);

            result.FileExists.Should().BeFalse();
            result.UploadRequired.Should().BeTrue();
            result.ReferenceId.Should().BeNull();
            result.NewReferenceCreated.Should().BeFalse();
            result.Error.Should().BeNull();
            context.FileRefs.Should().ContainSingle(reference => reference.Id == staleReference.Id);
            context.FileData.Should().BeEmpty();
            metadataStore.GetByAccessGroup(sessionGroup.Id).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CheckAndGetReferenceAsync_ReturnsDeterministicExistingAccessibleReference()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-deterministic-existing-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));

            var sessionUser = CreateUser("session");
            var firstGroup = CreateAccessGroup(sessionUser, "first");
            var secondGroup = CreateAccessGroup(sessionUser, "second");
            var laterReference = CreateFileReference(contentHash, secondGroup.Id);
            laterReference.Id = Guid.Parse("00000000-0000-0000-0000-000000000002");
            var earlierReference = CreateFileReference(contentHash, firstGroup.Id);
            earlierReference.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");

            context.Users.Add(sessionUser);
            context.AccessGroups.AddRange(firstGroup, secondGroup);
            context.FileRefs.AddRange(laterReference, earlierReference);
            await context.SaveChangesAsync();

            var service = CreateService(context, metadataStore, sessionUser.Id);
            var request = new HashCheckRequest
            {
                ContentHashHex = Convert.ToHexString(contentHash),
                FileExtension = "txt",
                FileName = "existing-copy",
                PublicViewing = false,
                PublicDownload = false
            };

            var result = await service.CheckAndGetReferenceAsync(request);

            result.FileExists.Should().BeTrue();
            result.ReferenceId.Should().Be(earlierReference.Id);
            result.NewReferenceCreated.Should().BeFalse();
            result.Error.Should().BeNull();
            context.FileRefs.Should().HaveCount(2);
            metadataStore.GetByFileHash(contentHash, "txt").Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CheckAndGetReferenceAsync_DoesNotUseStaleTokenAdminRoleForExistingPrivateReference()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-stale-admin-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));

            var sessionUser = CreateUser("session", isAdmin: false);
            var existingOwner = CreateUser("owner");
            var sessionGroup = CreateAccessGroup(sessionUser, "session");
            var existingGroup = CreateAccessGroup(existingOwner, "existing");
            var existingReference = CreateFileReference(contentHash, existingGroup.Id);
            var existingData = CreateFileData(existingReference, existingOwner.Id);
            existingData.Metadata = JsonDocument.Parse("""{"origin":"existing"}""");
            var existingMetadata = CreateMetadata(existingReference, existingGroup.Id);

            context.Users.AddRange(sessionUser, existingOwner);
            context.AccessGroups.AddRange(sessionGroup, existingGroup);
            context.FileRefs.Add(existingReference);
            context.FileData.Add(existingData);
            await context.SaveChangesAsync();
            metadataStore.AddMetadata(existingMetadata);

            var service = CreateService(context, metadataStore, new SessionDTO
            {
                LoggedIn = true,
                UserId = sessionUser.Id,
                Username = sessionUser.Username,
                IsAdmin = true,
                IsBanned = false,
                IsWhitelisted = true,
                Roles = ["Admin", "Whitelisted"],
                Claims = [],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-1),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            var request = new HashCheckRequest
            {
                ContentHashHex = Convert.ToHexString(contentHash),
                FileExtension = "txt",
                AccessGroupId = sessionGroup.Id,
                FileName = "demoted-copy",
                PublicViewing = false,
                PublicDownload = false
            };

            var result = await service.CheckAndGetReferenceAsync(request);

            result.FileExists.Should().BeTrue();
            result.ReferenceId.Should().NotBe(existingReference.Id);
            result.NewReferenceCreated.Should().BeTrue();
            result.Error.Should().BeNull();
            context.FileRefs.Should().HaveCount(2);
            context.FileData.Should().HaveCount(2);
            metadataStore.GetByAccessGroup(sessionGroup.Id)
                .Should()
                .ContainSingle(record => record.RefId == result.ReferenceId);
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CheckAndGetReferenceAsync_ReturnsFailureForRequestedAccessGroupOutsideCurrentUserMembership()
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

            var result = await service.CheckAndGetReferenceAsync(request);

            result.Error.Should().Be("User cannot create a file reference in the requested access group.");
            result.FileExists.Should().BeFalse();
            result.ReferenceId.Should().BeNull();
            result.NewReferenceCreated.Should().BeFalse();
            result.ContentHashHex.Should().Be(Convert.ToHexString(contentHash));

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
    public async Task CheckAndGetReferenceAsync_ReturnsFailureWhenDefaultAccessGroupDoesNotExist()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-no-default-group-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));

            var sessionUser = CreateUser("session");
            var existingOwner = CreateUser("owner");
            var existingGroup = CreateAccessGroup(existingOwner, "existing");
            var existingReference = CreateFileReference(contentHash, existingGroup.Id);
            var existingData = CreateFileData(existingReference, existingOwner.Id);
            var existingMetadata = CreateMetadata(existingReference, existingGroup.Id);

            context.Users.AddRange(sessionUser, existingOwner);
            context.AccessGroups.Add(existingGroup);
            context.FileRefs.Add(existingReference);
            context.FileData.Add(existingData);
            await context.SaveChangesAsync();
            metadataStore.AddMetadata(existingMetadata);

            var service = CreateService(context, metadataStore, sessionUser.Id);
            var request = new HashCheckRequest
            {
                ContentHashHex = Convert.ToHexString(contentHash),
                FileExtension = "txt",
                FileName = "default-group-copy",
                PublicViewing = false,
                PublicDownload = false
            };

            var result = await service.CheckAndGetReferenceAsync(request);

            result.Error.Should().Be("User has no access groups.");
            result.FileExists.Should().BeFalse();
            result.ReferenceId.Should().BeNull();
            result.NewReferenceCreated.Should().BeFalse();
            result.ContentHashHex.Should().Be(Convert.ToHexString(contentHash));

            metadataStore.GetByFileHash(contentHash, "txt")
                .Should()
                .ContainSingle(record => record.RefId == existingReference.Id);
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
    public async Task CheckAndGetReferenceAsync_UsesOldestOwnedAccessGroupWhenRequestOmitsAccessGroup()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-default-owned-group-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));

            var sessionUser = CreateUser("session");
            var existingOwner = CreateUser("owner");
            var existingGroup = CreateAccessGroup(existingOwner, "existing");
            var newerOwnedGroup = CreateAccessGroup(sessionUser, "newer");
            newerOwnedGroup.CreatedAt = DateTime.UtcNow.AddMinutes(-5);
            var olderOwnedGroup = CreateAccessGroup(sessionUser, "older");
            olderOwnedGroup.CreatedAt = DateTime.UtcNow.AddMinutes(-10);
            var existingReference = CreateFileReference(contentHash, existingGroup.Id);
            var existingData = CreateFileData(existingReference, existingOwner.Id);
            var existingMetadata = CreateMetadata(existingReference, existingGroup.Id);

            context.Users.AddRange(sessionUser, existingOwner);
            context.AccessGroups.AddRange(existingGroup, newerOwnedGroup, olderOwnedGroup);
            context.FileRefs.Add(existingReference);
            context.FileData.Add(existingData);
            await context.SaveChangesAsync();
            metadataStore.AddMetadata(existingMetadata);

            var service = CreateService(context, metadataStore, sessionUser.Id);
            var request = new HashCheckRequest
            {
                ContentHashHex = Convert.ToHexString(contentHash),
                FileExtension = "txt",
                FileName = "default-owned-group-copy",
                PublicViewing = false,
                PublicDownload = false
            };

            var result = await service.CheckAndGetReferenceAsync(request);

            result.FileExists.Should().BeTrue();
            result.NewReferenceCreated.Should().BeTrue();
            result.ReferenceId.Should().NotBeNull();
            result.Error.Should().BeNull();

            context.FileRefs
                .Single(reference => reference.Id == result.ReferenceId)
                .AccessGroupId
                .Should()
                .Be(olderOwnedGroup.Id);
            metadataStore.GetByAccessGroup(olderOwnedGroup.Id)
                .Should()
                .ContainSingle(record => record.RefId == result.ReferenceId);
            metadataStore.GetByAccessGroup(newerOwnedGroup.Id).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CheckAndGetReferenceAsync_ReturnsFailureForEmptyRequestedAccessGroup()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-empty-group-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));

            var sessionUser = CreateUser("session");
            var existingOwner = CreateUser("owner");
            var sessionGroup = CreateAccessGroup(sessionUser, "session");
            var existingGroup = CreateAccessGroup(existingOwner, "existing");
            var existingReference = CreateFileReference(contentHash, existingGroup.Id);
            var existingData = CreateFileData(existingReference, existingOwner.Id);
            var existingMetadata = CreateMetadata(existingReference, existingGroup.Id);

            context.Users.AddRange(sessionUser, existingOwner);
            context.AccessGroups.AddRange(sessionGroup, existingGroup);
            context.FileRefs.Add(existingReference);
            context.FileData.Add(existingData);
            await context.SaveChangesAsync();
            metadataStore.AddMetadata(existingMetadata);

            var service = CreateService(context, metadataStore, sessionUser.Id);
            var request = new HashCheckRequest
            {
                ContentHashHex = Convert.ToHexString(contentHash),
                FileExtension = "txt",
                AccessGroupId = Guid.Empty,
                FileName = "empty-group-copy",
                PublicViewing = false,
                PublicDownload = false
            };

            var result = await service.CheckAndGetReferenceAsync(request);

            result.Error.Should().Be("Access group id must be provided.");
            result.FileExists.Should().BeFalse();
            result.ReferenceId.Should().BeNull();
            result.NewReferenceCreated.Should().BeFalse();
            result.ContentHashHex.Should().Be(Convert.ToHexString(contentHash));

            metadataStore.GetByFileHash(contentHash, "txt")
                .Should()
                .ContainSingle(record => record.RefId == existingReference.Id);
            metadataStore.GetByAccessGroup(sessionGroup.Id).Should().BeEmpty();
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
    public async Task CheckAndGetReferenceAsync_NormalizesMetadataForNewReference()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-normalized-metadata-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
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
            await context.SaveChangesAsync();
            metadataStore.AddMetadata(existingMetadata);

            var fixedTimeProvider = new FixedTimeProvider(FixedNow);
            var service = CreateService(context, metadataStore, sessionUser.Id, fixedTimeProvider);
            var request = new HashCheckRequest
            {
                ContentHashHex = Convert.ToHexString(contentHash),
                FileExtension = "txt",
                AccessGroupId = targetGroup.Id,
                FileName = "  normalized-copy  ",
                Description = "  normalized dedupe description  ",
                Tags = ["  copy  ", "shared"],
                Categories = ["  tests  "],
                PublicViewing = false,
                PublicDownload = false
            };

            var result = await service.CheckAndGetReferenceAsync(request);

            result.FileExists.Should().BeTrue();
            result.NewReferenceCreated.Should().BeTrue();
            result.ReferenceId.Should().NotBeNull();
            result.Error.Should().BeNull();

            var newData = context.FileData.Single(fileData => fileData.FileReferenceId == result.ReferenceId);
            newData.Name.Should().Be("normalized-copy");
            newData.Description.Should().Be("normalized dedupe description");
            newData.Tags.Should().Equal("copy", "shared");
            newData.Categories.Should().Equal("tests");
            newData.Metadata.RootElement.GetRawText().Should().Be("{}");
            newData.SavedAt.Should().Be(FixedNow.UtcDateTime);
            newData.LastModifiedAt.Should().Be(FixedNow.UtcDateTime);
            metadataStore.GetByAccessGroup(targetGroup.Id)
                .Should()
                .ContainSingle(record => record.RefId == result.ReferenceId)
                .Which.CreatedAt.Should().Be(FixedNow.UtcDateTime);
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CheckAndGetReferenceAsync_DoesNotCopyInaccessibleReferenceMetadataToNewReference()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-private-metadata-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));

            var sessionUser = CreateUser("session");
            var existingOwner = CreateUser("owner");
            var targetGroup = CreateAccessGroup(sessionUser, "target");
            var existingGroup = CreateAccessGroup(existingOwner, "existing");
            var existingReference = CreateFileReference(contentHash, existingGroup.Id);
            var existingData = CreateFileData(existingReference, existingOwner.Id);
            existingData.Name = "private source name";
            existingData.Description = "private source description";
            existingData.Tags = ["private-tag"];
            existingData.Categories = ["private-category"];
            existingData.Metadata = JsonDocument.Parse("""{"private":"metadata"}""");
            var existingMetadata = CreateMetadata(existingReference, existingGroup.Id);

            context.Users.AddRange(sessionUser, existingOwner);
            context.AccessGroups.AddRange(targetGroup, existingGroup);
            context.FileRefs.Add(existingReference);
            context.FileData.Add(existingData);
            await context.SaveChangesAsync();
            metadataStore.AddMetadata(existingMetadata);

            var service = CreateService(context, metadataStore, sessionUser.Id);
            var request = new HashCheckRequest
            {
                ContentHashHex = Convert.ToHexString(contentHash),
                FileExtension = "txt",
                AccessGroupId = targetGroup.Id,
                PublicViewing = false,
                PublicDownload = false
            };

            var result = await service.CheckAndGetReferenceAsync(request);

            result.FileExists.Should().BeTrue();
            result.NewReferenceCreated.Should().BeTrue();
            result.ReferenceId.Should().NotBeNull();
            result.Error.Should().BeNull();

            var newData = context.FileData.Single(fileData => fileData.FileReferenceId == result.ReferenceId);
            newData.Name.Should().Be("Unnamed");
            newData.Description.Should().BeEmpty();
            newData.Tags.Should().BeEmpty();
            newData.Categories.Should().BeEmpty();
            newData.Metadata.RootElement.GetRawText().Should().Be("{}");
            newData.SizeInBytes.Should().Be(existingData.SizeInBytes);
            newData.Checksum.Should().Be(existingData.Checksum);
            newData.MimeType.Should().Be(existingData.MimeType);
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CheckAndGetReferenceAsync_ReturnsFailureForInvalidNewReferenceMetadata()
    {
        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes($"dedupe-invalid-metadata-{Guid.NewGuid()}"));
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
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
            await context.SaveChangesAsync();
            metadataStore.AddMetadata(existingMetadata);

            var service = CreateService(context, metadataStore, sessionUser.Id);
            var request = new HashCheckRequest
            {
                ContentHashHex = Convert.ToHexString(contentHash),
                FileExtension = "txt",
                AccessGroupId = targetGroup.Id,
                FileName = "invalid-copy",
                Tags = [" "],
                PublicViewing = false,
                PublicDownload = false
            };

            var result = await service.CheckAndGetReferenceAsync(request);

            result.Error.Should().Be("Tags values must be provided.");
            result.FileExists.Should().BeFalse();
            result.ReferenceId.Should().BeNull();
            result.NewReferenceCreated.Should().BeFalse();
            result.ContentHashHex.Should().Be(Convert.ToHexString(contentHash));

            metadataStore.GetByFileHash(contentHash, "txt")
                .Should()
                .ContainSingle(record => record.RefId == existingReference.Id);
            metadataStore.GetByAccessGroup(targetGroup.Id).Should().BeEmpty();
            context.FileRefs.Count().Should().Be(1);
            context.FileData.Count().Should().Be(1);
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
        Guid sessionUserId,
        TimeProvider? timeProvider = null)
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
        }, timeProvider);
    }

    private static FileDeduplicationService CreateService(
        BaseDataContext context,
        MetadataStore metadataStore,
        SessionDTO session,
        TimeProvider? timeProvider = null)
    {
        return new FileDeduplicationService(
            context,
            metadataStore,
            new UserSessionService(context, new TestRequestSessionAccessor(session)),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context, timeProvider),
            NullLogger<FileDeduplicationService>.Instance,
            timeProvider);
    }

    private sealed class TestRequestSessionAccessor : IRequestSessionAccessor
    {
        private readonly SessionDTO _session;

        public TestRequestSessionAccessor(SessionDTO session)
        {
            _session = session;
        }

        public SessionDTO? GetSession() => _session;
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

    private static UserDB CreateUser(
        string username,
        bool isAdmin = false,
        bool isWhitelisted = true,
        bool isBanned = false)
    {
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = [1],
            PasswordSalt = [2],
            IsAdmin = isAdmin,
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

    private SavedFileReferenceDB CreateFileReference(
        byte[] contentHash,
        Guid accessGroupId,
        bool createStoredContent = true)
    {
        if (createStoredContent)
            CreateStoredContentFile(contentHash);

        return new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = contentHash,
            FileExtension = FileExtensionType._TXT,
            PublicDownload = false,
            AccessGroupId = accessGroupId
        };
    }

    private void CreateStoredContentFile(byte[] contentHash)
    {
        string contentPath = FileContentUtils.GetFullPath(contentHash, "txt");
        string? contentDirectory = Path.GetDirectoryName(contentPath);
        if (string.IsNullOrWhiteSpace(contentDirectory))
            throw new InvalidOperationException("Could not resolve test content directory.");

        Directory.CreateDirectory(contentDirectory);
        File.WriteAllText(contentPath, "dedupe stored content placeholder");
        _storedContentPaths.Add(contentPath);
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

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
