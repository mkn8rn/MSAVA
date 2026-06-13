using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Import;
using MSAVA_INF.Contexts;
using MSAVA_INF.Managers;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class ProviderImportServiceTests
{
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
            var service = new GoogleDriveImportService(
                CreatePersistenceService(context, metadataStore),
                new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
                httpClientFactory);
            var dto = new FetchFileGoogleDriveDTO
            {
                FileUrl = "abcDEF12345",
                AccessGroupId = Guid.NewGuid()
            };

            Func<Task> act = () => service.ImportAsync(dto);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Google Drive initial request failed 400: drive failure");

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
            var service = new OneDriveImportService(
                CreatePersistenceService(context, metadataStore),
                new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
                httpClientFactory);
            var dto = new FetchFileFromOneDriveDTO
            {
                FileUrl = "https://1drv.ms/u/s!abcDEF12345",
                AccessGroupId = Guid.NewGuid()
            };

            Func<Task> act = () => service.ImportAsync(dto);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("OneDrive download failed 400: onedrive failure");

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
                httpClientFactory);
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
                httpClientFactory);
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
            var service = new GoogleDriveImportService(
                CreatePersistenceService(context, metadataStore, logger),
                new ServiceLogger(logger, context),
                httpClientFactory);
            var dto = new FetchFileGoogleDriveDTO
            {
                FileUrl = "abcDEF12345",
                AccessGroupId = Guid.NewGuid()
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
            var service = new OneDriveImportService(
                CreatePersistenceService(context, metadataStore, logger),
                new ServiceLogger(logger, context),
                httpClientFactory);
            var dto = new FetchFileFromOneDriveDTO
            {
                FileUrl = "https://1drv.ms/u/s!abcDEF12345",
                AccessGroupId = Guid.NewGuid()
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
    public async Task YouTubeImportAsync_RejectsEmptyDownloadSelectionBeforeParsingUrl()
    {
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = new YouTubeImportService(
                CreatePersistenceService(context, metadataStore),
                new ServiceLogger(NullLogger<ServiceLogger>.Instance, context));
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
        ILogger<ServiceLogger>? serviceLogger = null)
    {
        return new FilePersistenceService(
            context,
            new FileManager(metadataStore),
            new NullHttpContextAccessor(),
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

    private sealed class NullHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
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
