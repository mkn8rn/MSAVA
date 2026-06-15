using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Import;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Contexts;
using MSAVA_INF.Managers;
using MSAVA_INF.Models;
using MSAVA_INF.Utils;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class ProviderImportServiceTests
{
    [Test]
    public async Task ProviderHttpFailure_UsesReasonPhraseWhenBodyIsMissing()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound);

        Func<Task> act = () => ProviderHttpFailure.ThrowAsync("Provider request", response, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Provider request failed 404 (Not Found)");

        exception.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ProviderHttpFailure_TruncatesLongErrorBody()
    {
        string errorBody = new('x', 2050);
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(errorBody)
        };
        string expectedBody = new('x', 2048);

        Func<Task> act = () => ProviderHttpFailure.ThrowAsync("Provider download", response, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage($"Provider download failed 502: {expectedBody}");

        exception.Which.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Test]
    public async Task GoogleDriveImportAsync_UsesInjectedHttpClientFactory()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("drive failure")
            });
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (user, accessGroup) = SeedUserWithAccessGroup(context);
            var service = new GoogleDriveImportService(
                CreatePersistenceService(context, metadataStore, session: CreateSession(user.Id)),
                new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
                httpClientFactory,
                NullLogger<GoogleDriveImportService>.Instance);
            var dto = new FetchFileGoogleDriveDTO
            {
                FileUrl = "abcDEF12345",
                AccessGroupId = accessGroup.Id
            };

            Func<Task> act = () => service.ImportAsync(dto);

            var exception = await act.Should().ThrowAsync<HttpRequestException>()
                .WithMessage("Google Drive initial request failed 400: drive failure");

            exception.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            httpClientFactory.WasCalled.Should().BeTrue();
            handler.Requests.Should().ContainSingle();
            handler.Requests[0].RequestUri!.Host.Should().Be("drive.google.com");
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GoogleDriveImportAsync_RejectsMalformedUrlBeforeCreatingHttpClient()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = new GoogleDriveImportService(
                CreatePersistenceService(context, metadataStore),
                new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
                httpClientFactory,
                NullLogger<GoogleDriveImportService>.Instance);
            var dto = new FetchFileGoogleDriveDTO
            {
                FileUrl = "https://[invalid-drive-url",
                AccessGroupId = Guid.NewGuid()
            };

            Func<Task> act = () => service.ImportAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("Could not extract Google Drive file id from the provided FileUrl.*");

            httpClientFactory.WasCalled.Should().BeFalse();
            handler.Requests.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GoogleDriveImportAsync_RejectsMissingSessionBeforeCreatingHttpClient()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = new GoogleDriveImportService(
                CreatePersistenceService(context, metadataStore),
                new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
                httpClientFactory,
                NullLogger<GoogleDriveImportService>.Instance);
            var dto = new FetchFileGoogleDriveDTO
            {
                FileUrl = "abcDEF12345",
                AccessGroupId = Guid.NewGuid()
            };

            Func<Task> act = () => service.ImportAsync(dto);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("User session not found.");
            httpClientFactory.WasCalled.Should().BeFalse();
            handler.Requests.Should().BeEmpty();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task OneDriveImportAsync_UsesInjectedHttpClientFactory()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("onedrive failure")
            });
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (user, accessGroup) = SeedUserWithAccessGroup(context);
            var service = new OneDriveImportService(
                CreatePersistenceService(context, metadataStore, session: CreateSession(user.Id)),
                new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
                httpClientFactory,
                NullLogger<OneDriveImportService>.Instance);
            var dto = new FetchFileFromOneDriveDTO
            {
                FileUrl = "https://1drv.ms/u/s!abcDEF12345",
                AccessGroupId = accessGroup.Id
            };

            Func<Task> act = () => service.ImportAsync(dto);

            var exception = await act.Should().ThrowAsync<HttpRequestException>()
                .WithMessage("OneDrive download failed 400: onedrive failure");

            exception.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            httpClientFactory.WasCalled.Should().BeTrue();
            handler.Requests.Should().ContainSingle();
            handler.Requests[0].RequestUri!.Host.Should().Be("api.onedrive.com");
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task OneDriveImportAsync_RejectsRelativeUrlBeforeCreatingHttpClient()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = new OneDriveImportService(
                CreatePersistenceService(context, metadataStore),
                new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
                httpClientFactory,
                NullLogger<OneDriveImportService>.Instance);
            var dto = new FetchFileFromOneDriveDTO
            {
                FileUrl = "shared/file.txt",
                AccessGroupId = Guid.NewGuid()
            };

            Func<Task> act = () => service.ImportAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("FileUrl must be an absolute HTTP or HTTPS URL.*");

            httpClientFactory.WasCalled.Should().BeFalse();
            handler.Requests.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task OneDriveImportAsync_RejectsNonHttpUrlBeforeCreatingHttpClient()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = new OneDriveImportService(
                CreatePersistenceService(context, metadataStore),
                new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
                httpClientFactory,
                NullLogger<OneDriveImportService>.Instance);
            var dto = new FetchFileFromOneDriveDTO
            {
                FileUrl = "file:///C:/temp/shared.txt",
                AccessGroupId = Guid.NewGuid()
            };

            Func<Task> act = () => service.ImportAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("FileUrl must be an absolute HTTP or HTTPS URL.*");

            httpClientFactory.WasCalled.Should().BeFalse();
            handler.Requests.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task OneDriveImportAsync_RejectsMissingSessionBeforeCreatingHttpClient()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = new OneDriveImportService(
                CreatePersistenceService(context, metadataStore),
                new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
                httpClientFactory,
                NullLogger<OneDriveImportService>.Instance);
            var dto = new FetchFileFromOneDriveDTO
            {
                FileUrl = "https://1drv.ms/u/s!abcDEF12345",
                AccessGroupId = Guid.NewGuid()
            };

            Func<Task> act = () => service.ImportAsync(dto);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("User session not found.");
            httpClientFactory.WasCalled.Should().BeFalse();
            handler.Requests.Should().BeEmpty();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GoogleDriveImportAsync_DeletesTempFileWhenFinalDownloadFails()
    {
        var responseIndex = 0;
        var handler = new RecordingHttpMessageHandler(_ =>
        {
            responseIndex++;
            if (responseIndex == 1)
                return CreateResponse(HttpStatusCode.OK, "application/octet-stream", "initial ok");

            return CreateResponse(HttpStatusCode.BadGateway, "text/plain", "download failure");
        });
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();
        var logger = new CapturingLogger<ServiceLogger>();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (user, accessGroup) = SeedUserWithAccessGroup(context);
            var service = new GoogleDriveImportService(
                CreatePersistenceService(context, metadataStore, logger, CreateSession(user.Id)),
                new ServiceLogger(logger, context),
                httpClientFactory,
                NullLogger<GoogleDriveImportService>.Instance);
            var dto = new FetchFileGoogleDriveDTO
            {
                FileUrl = "abcDEF12345",
                AccessGroupId = accessGroup.Id
            };

            Func<Task> act = () => service.ImportAsync(dto);

            var exception = await act.Should().ThrowAsync<HttpRequestException>()
                .WithMessage("Google Drive download failed 502: download failure");

            exception.Which.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            var tempFilePath = GetLoggedTempFilePath(logger);
            File.Exists(tempFilePath).Should().BeFalse();
            handler.Requests.Should().HaveCount(2);
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task GoogleDriveImportAsync_DeletesTempFileWhenDownloadReturnsHtml()
    {
        var responseIndex = 0;
        var handler = new RecordingHttpMessageHandler(_ =>
        {
            responseIndex++;
            if (responseIndex == 1)
                return CreateResponse(HttpStatusCode.OK, "application/octet-stream", "initial ok");

            return CreateResponse(HttpStatusCode.OK, "text/html", "<html>not a file</html>");
        });
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();
        var logger = new CapturingLogger<ServiceLogger>();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (user, accessGroup) = SeedUserWithAccessGroup(context);
            var service = new GoogleDriveImportService(
                CreatePersistenceService(context, metadataStore, logger, CreateSession(user.Id)),
                new ServiceLogger(logger, context),
                httpClientFactory,
                NullLogger<GoogleDriveImportService>.Instance);
            var dto = new FetchFileGoogleDriveDTO
            {
                FileUrl = "abcDEF12345",
                AccessGroupId = accessGroup.Id
            };

            Func<Task> act = () => service.ImportAsync(dto);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Google Drive returned HTML instead of file content.");

            var tempFilePath = GetLoggedTempFilePath(logger);
            File.Exists(tempFilePath).Should().BeFalse();
            handler.Requests.Should().HaveCount(2);
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task OneDriveImportAsync_DeletesTempFileWhenContentCopyFails()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new ThrowingReadStream())
            };
            response.Content.Headers.ContentType = new("text/plain");
            return response;
        });
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();
        var logger = new CapturingLogger<ServiceLogger>();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (user, accessGroup) = SeedUserWithAccessGroup(context);
            var service = new OneDriveImportService(
                CreatePersistenceService(context, metadataStore, logger, CreateSession(user.Id)),
                new ServiceLogger(logger, context),
                httpClientFactory,
                NullLogger<OneDriveImportService>.Instance);
            var dto = new FetchFileFromOneDriveDTO
            {
                FileUrl = "https://1drv.ms/u/s!abcDEF12345",
                AccessGroupId = accessGroup.Id
            };

            Func<Task> act = () => service.ImportAsync(dto);

            await act.Should().ThrowAsync<IOException>()
                .WithMessage("Simulated provider stream failure.");

            var tempFilePath = GetLoggedTempFilePath(logger);
            File.Exists(tempFilePath).Should().BeFalse();
            handler.Requests.Should().ContainSingle();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task OneDriveImportAsync_UsesProviderFallbackNameWhenResponseHasNoFileName()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            CreateResponse(HttpStatusCode.OK, "text/plain", "downloaded text content"));
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();
        var logger = new CapturingLogger<ServiceLogger>();
        string? contentPath = null;

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var owner = CreateUser("onedrive-owner");
            var accessGroup = CreateAccessGroup(owner, "OneDrive Imports");
            context.Users.Add(owner);
            context.AccessGroups.Add(accessGroup);
            await context.SaveChangesAsync();

            var service = new OneDriveImportService(
                CreatePersistenceService(context, metadataStore, logger, CreateSession(owner.Id)),
                new ServiceLogger(logger, context),
                httpClientFactory,
                NullLogger<OneDriveImportService>.Instance);
            var dto = new FetchFileFromOneDriveDTO
            {
                FileUrl = "https://1drv.ms/u/s!abcDEF12345",
                AccessGroupId = accessGroup.Id,
                Description = "Description should stay separate",
                Tags = ["onedrive"],
                Categories = ["imports"],
                PublicViewing = true,
                PublicDownload = false
            };

            var fileRefId = await service.ImportAsync(dto);

            var fileRef = context.FileRefs.Single(fileReference => fileReference.Id == fileRefId);
            var fileData = context.FileData.Single(fileData => fileData.FileReferenceId == fileRefId);
            contentPath = FileContentUtils.GetFullPath(fileRef.FileHash, fileRef.FileExtension.ToString());
            fileData.Name.Should().Be("OneDrive File");
            fileData.Description.Should().Be("Description should stay separate");
            fileData.FileExtension.Should().Be("txt");
            fileData.Tags.Should().Equal("onedrive");
            fileData.Categories.Should().Equal("imports");
            fileData.PublicViewing.Should().BeTrue();
            fileRef.PublicDownload.Should().BeFalse();
            fileRef.AccessGroupId.Should().Be(accessGroup.Id);
            handler.Requests.Should().ContainSingle();
            handler.Requests[0].RequestUri!.Host.Should().Be("api.onedrive.com");
        }
        finally
        {
            if (contentPath is not null)
                DeleteFileIfPresent(contentPath);

            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task YouTubeImportAsync_RejectsEmptyDownloadSelectionBeforeParsingUrl()
    {
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = new YouTubeImportService(
                CreatePersistenceService(context, metadataStore),
                new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
                NullLogger<YouTubeImportService>.Instance);
            var dto = new FetchFileYouTubeDTO
            {
                YouTubeUrl = "not a youtube video id",
                AccessGroupId = Guid.NewGuid(),
                DownloadVideo = false,
                DownloadAudio = false
            };

            Func<Task> act = () => service.ImportAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("At least one YouTube stream type must be selected.*");
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task YouTubeImportAsync_RejectsMissingSessionBeforeRequestingManifest()
    {
        var metadataDirectory = CreateTempDirectory();
        var youTubeClient = new ThrowingYouTubeDownloadClient();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = new YouTubeImportService(
                CreatePersistenceService(context, metadataStore),
                new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
                NullLogger<YouTubeImportService>.Instance,
                youTubeClient);
            var dto = new FetchFileYouTubeDTO
            {
                YouTubeUrl = "https://www.youtube.com/watch?v=abcDEF12345",
                AccessGroupId = Guid.NewGuid(),
                DownloadVideo = true,
                DownloadAudio = true
            };

            Func<Task> act = () => service.ImportAsync(dto);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("User session not found.");
            youTubeClient.ManifestCalls.Should().Be(0);
            youTubeClient.CopyCalls.Should().Be(0);
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task YouTubeImportAsync_DeletesTempFileWhenStreamCopyFails()
    {
        var metadataDirectory = CreateTempDirectory();
        var logger = new CapturingLogger<ServiceLogger>();
        var youTubeClient = new ThrowingYouTubeDownloadClient();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (user, accessGroup) = SeedUserWithAccessGroup(context);
            var service = new YouTubeImportService(
                CreatePersistenceService(context, metadataStore, logger, CreateSession(user.Id)),
                new ServiceLogger(logger, context),
                NullLogger<YouTubeImportService>.Instance,
                youTubeClient);
            var dto = new FetchFileYouTubeDTO
            {
                YouTubeUrl = "https://www.youtube.com/watch?v=abcDEF12345",
                AccessGroupId = accessGroup.Id,
                DownloadVideo = true,
                DownloadAudio = true
            };

            Func<Task> act = () => service.ImportAsync(dto);

            await act.Should().ThrowAsync<IOException>()
                .WithMessage("Simulated YouTube stream failure.");

            var tempFilePath = GetLoggedTempFilePath(logger);
            File.Exists(tempFilePath).Should().BeFalse();
            youTubeClient.CopyCalls.Should().Be(1);
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public void CreateFfmpegStartInfo_UsesArgumentListWithoutShellExecution()
    {
        var videoPath = Path.Combine("C:\\temp", "video input.webm");
        var audioPath = Path.Combine("C:\\temp", "audio input.webm");
        var outputPath = Path.Combine("C:\\temp", "muxed output.mp4");

        var startInfo = YouTubeImportService.CreateFfmpegStartInfo(
            "webm",
            videoPath,
            "webm",
            audioPath,
            outputPath);

        startInfo.FileName.Should().Be("ffmpeg");
        startInfo.UseShellExecute.Should().BeFalse();
        startInfo.RedirectStandardError.Should().BeTrue();
        startInfo.CreateNoWindow.Should().BeTrue();
        startInfo.Arguments.Should().BeEmpty();
        startInfo.ArgumentList.Should().Equal(
            "-y",
            "-f",
            "webm",
            "-i",
            videoPath,
            "-f",
            "webm",
            "-i",
            audioPath,
            "-c:v",
            "copy",
            "-c:a",
            "aac",
            "-shortest",
            "-f",
            "mp4",
            outputPath);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string contentType, string body)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body)
        };
        response.Content.Headers.ContentType = new(contentType);
        return response;
    }

    private static string GetLoggedTempFilePath(CapturingLogger<ServiceLogger> logger)
    {
        var logMessage = logger.Messages.Single(message => message.Contains("temp path ", StringComparison.Ordinal));
        return logMessage[(logMessage.LastIndexOf("temp path ", StringComparison.Ordinal) + "temp path ".Length)..];
    }

    private static FilePersistenceService CreatePersistenceService(
        BaseDataContext context,
        MetadataStore metadataStore,
        ILogger<ServiceLogger>? serviceLogger = null,
        SessionDTO? session = null)
    {
        return new FilePersistenceService(
            context,
            new FileManager(metadataStore, NullLogger<FileManager>.Instance),
            new TestRequestSessionAccessor(session),
            new ServiceLogger(serviceLogger ?? NullLogger<ServiceLogger>.Instance, context),
            NullLogger<FilePersistenceService>.Instance);
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

    private static SessionDTO CreateSession(Guid userId)
    {
        return new SessionDTO
        {
            LoggedIn = true,
            UserId = userId,
            Username = "provider-import-test",
            IsWhitelisted = true,
            Roles = ["Whitelisted"],
            AccessGroups = []
        };
    }

    private static (UserDB User, AccessGroupDB AccessGroup) SeedUserWithAccessGroup(BaseDataContext context)
    {
        var user = CreateUser("provider-import-owner");
        var accessGroup = CreateAccessGroup(user, "Provider Imports");

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

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public RecordingHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public bool WasCalled { get; private set; }

        public HttpClient CreateClient(string name)
        {
            WasCalled = true;
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class ThrowingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new IOException("Simulated provider stream failure.");
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException<int>(new IOException("Simulated provider stream failure."));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ThrowingYouTubeDownloadClient : IYouTubeDownloadClient
    {
        private static readonly byte[] PartialContent = Encoding.UTF8.GetBytes("partial youtube content");

        public int ManifestCalls { get; private set; }
        public int CopyCalls { get; private set; }

        public Task<YouTubeDownloadManifest> GetDownloadManifestAsync(
            string youtubeUrl,
            CancellationToken cancellationToken)
        {
            ManifestCalls++;
            return Task.FromResult(new YouTubeDownloadManifest(
                "Test YouTube Video",
                [new YouTubeStreamInfo(new object(), "mp4", "720p", 720, 1_500)],
                [],
                []));
        }

        public async Task CopyToAsync(
            YouTubeStreamInfo streamInfo,
            Stream destination,
            CancellationToken cancellationToken)
        {
            CopyCalls++;
            await destination.WriteAsync(PartialContent, cancellationToken);
            throw new IOException("Simulated YouTube stream failure.");
        }
    }

    private sealed class TestRequestSessionAccessor(SessionDTO? session = null) : IRequestSessionAccessor
    {
        public SessionDTO? GetSession() => session;
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
