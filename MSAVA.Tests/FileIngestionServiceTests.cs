using System.Net;
using Microsoft.AspNetCore.Http;
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

public class FileIngestionServiceTests
{
    [Test]
    public async Task CreateFileFromUrlAsync_RejectsRelativeUrlBeforeCreatingHttpClient()
    {
        var httpClientFactory = new RecordingHttpClientFactory();
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, httpClientFactory);
            var dto = CreateUrlDto("files/sample.txt");

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("FileUrl must be an absolute HTTP or HTTPS URL.*");

            httpClientFactory.WasCalled.Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromUrlAsync_RejectsNonHttpUrlBeforeCreatingHttpClient()
    {
        var httpClientFactory = new RecordingHttpClientFactory();
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(context, metadataStore, httpClientFactory);
            var dto = CreateUrlDto("file:///C:/temp/sample.txt");

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("FileUrl must be an absolute HTTP or HTTPS URL.*");

            httpClientFactory.WasCalled.Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [TestCase("http://localhost/file.txt")]
    [TestCase("http://api.localhost/file.txt")]
    [TestCase("http://127.0.0.1/file.txt")]
    [TestCase("http://10.0.0.5/file.txt")]
    [TestCase("http://172.16.1.5/file.txt")]
    [TestCase("http://192.168.1.10/file.txt")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    [TestCase("http://[::1]/file.txt")]
    [TestCase("http://[fd00::1]/file.txt")]
    [TestCase("https://metadata.google.internal/computeMetadata/v1/")]
    public async Task CreateFileFromUrlAsync_RejectsUnsafeHostBeforeCreatingHttpClient(string fileUrl)
    {
        var httpClientFactory = new RecordingHttpClientFactory();
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (user, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, httpClientFactory, session: CreateSession(user.Id));
            var dto = CreateUrlDto(fileUrl, accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("FileUrl host is not allowed for server-side ingestion.*");

            httpClientFactory.WasCalled.Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromUrlAsync_RejectsHostResolvedToUnsafeAddressBeforeCreatingHttpClient()
    {
        var httpClientFactory = new RecordingHttpClientFactory();
        var metadataDirectory = CreateTempDirectory();
        using var cancellationTokenSource = new CancellationTokenSource();
        string? resolvedHost = null;
        CancellationToken resolverCancellationToken = default;

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (user, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(
                context,
                metadataStore,
                httpClientFactory,
                session: CreateSession(user.Id),
                hostAddressResolver: (host, cancellationToken) =>
                {
                    resolvedHost = host;
                    resolverCancellationToken = cancellationToken;
                    return Task.FromResult(new[] { IPAddress.Parse("127.0.0.1") });
                });
            var dto = CreateUrlDto("https://files.example.test/sample.txt", accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto, cancellationTokenSource.Token);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("FileUrl host resolves to an address that is not allowed for server-side ingestion.*");

            resolvedHost.Should().Be("files.example.test");
            resolverCancellationToken.Should().Be(cancellationTokenSource.Token);
            httpClientFactory.WasCalled.Should().BeFalse();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromUrlAsync_RejectsMissingSessionBeforeResolvingHostOrCreatingHttpClient()
    {
        var httpClientFactory = new RecordingHttpClientFactory();
        var metadataDirectory = CreateTempDirectory();
        bool resolverCalled = false;

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var service = CreateService(
                context,
                metadataStore,
                httpClientFactory,
                hostAddressResolver: (_, _) =>
                {
                    resolverCalled = true;
                    return Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
                });
            var dto = CreateUrlDto("https://files.example.test/sample.txt");

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("User session not found.");
            resolverCalled.Should().BeFalse();
            httpClientFactory.WasCalled.Should().BeFalse();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromUrlAsync_ThrowsHttpRequestExceptionWhenRemoteDownloadFails()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("missing file")
            });
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (user, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, httpClientFactory, session: CreateSession(user.Id));
            var dto = CreateUrlDto("https://example.com/files/missing.txt", accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto);

            var exception = await act.Should().ThrowAsync<HttpRequestException>()
                .WithMessage("File URL download failed 404: missing file");

            exception.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
            httpClientFactory.WasCalled.Should().BeTrue();
            httpClientFactory.ClientName.Should().Be(FileIngestionService.RemoteFileHttpClientName);
            handler.RequestUri.Should().Be(new Uri(dto.FileUrl));
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromUrlAsync_TruncatesRemoteErrorBodyWithoutReadingEntireBody()
    {
        var errorStream = new CountingRepeatingReadStream((byte)'x', 100_000);
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StreamContent(errorStream)
            });
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();
        string expectedBody = new('x', 2048);

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (user, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, httpClientFactory, session: CreateSession(user.Id));
            var dto = CreateUrlDto("https://example.com/files/error.txt", accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto);

            var exception = await act.Should().ThrowAsync<HttpRequestException>()
                .WithMessage($"File URL download failed 502: {expectedBody}");

            exception.Which.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            errorStream.BytesRead.Should().BeLessThan(errorStream.TotalLength);
            httpClientFactory.WasCalled.Should().BeTrue();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromUrlAsync_TreatsRedirectAsRemoteFailure()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers =
                {
                    Location = new Uri("http://127.0.0.1/private.txt")
                }
            });
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (user, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(context, metadataStore, httpClientFactory, session: CreateSession(user.Id));
            var dto = CreateUrlDto("https://example.com/files/redirect.txt", accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto);

            var exception = await act.Should().ThrowAsync<HttpRequestException>()
                .WithMessage("File URL download failed 302 (Found).");

            exception.Which.StatusCode.Should().Be(HttpStatusCode.Redirect);
            httpClientFactory.ClientName.Should().Be(FileIngestionService.RemoteFileHttpClientName);
            handler.RequestUri.Should().Be(new Uri(dto.FileUrl));
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromUrlAsync_RejectsOversizeDeclaredContentLengthBeforeReadingBody()
    {
        var responseContent = new DeclaredLengthContent(5);
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = responseContent
            });
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (user, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(
                context,
                metadataStore,
                httpClientFactory,
                session: CreateSession(user.Id),
                maximumFileSizeBytes: 4);
            var dto = CreateUrlDto("https://example.com/files/large.txt", accessGroup.Id);

            Func<Task> act = () => service.CreateFileFromUrlAsync(dto);

            await act.Should().ThrowAsync<FileTooLargeException>()
                .WithMessage("File size 5 bytes exceeds the maximum allowed size of 4 bytes.");

            responseContent.SerializeWasCalled.Should().BeFalse();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    [Test]
    public async Task CreateFileFromFormFileAsync_RejectsOversizeDeclaredLengthBeforeOpeningFile()
    {
        var httpClientFactory = new RecordingHttpClientFactory();
        var metadataDirectory = CreateTempDirectory();

        try
        {
            using var context = CreateContext();
            using var metadataStore = new MetadataStore(Path.Combine(metadataDirectory, "metadata.db"));
            var (user, accessGroup) = SeedUserWithAccessGroup(context);
            var service = CreateService(
                context,
                metadataStore,
                httpClientFactory,
                session: CreateSession(user.Id),
                maximumFileSizeBytes: 4);
            var formFile = new RecordingFormFile(5);
            var dto = new SaveFileFromFormFileDTO
            {
                FileName = "large",
                FileExtension = "txt",
                FormFile = formFile,
                AccessGroupId = accessGroup.Id,
                Tags = [],
                Categories = [],
                Description = string.Empty,
                PublicViewing = false,
                PublicDownload = false
            };

            Func<Task> act = () => service.CreateFileFromFormFileAsync(dto);

            await act.Should().ThrowAsync<FileTooLargeException>()
                .WithMessage("File size 5 bytes exceeds the maximum allowed size of 4 bytes.");

            formFile.OpenReadStreamWasCalled.Should().BeFalse();
            context.FileRefs.Should().BeEmpty();
            context.FileData.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfPresent(metadataDirectory);
        }
    }

    private static SaveFileFromUrlDTO CreateUrlDto(string fileUrl)
    {
        return CreateUrlDto(fileUrl, Guid.NewGuid());
    }

    private static SaveFileFromUrlDTO CreateUrlDto(string fileUrl, Guid accessGroupId)
    {
        return new SaveFileFromUrlDTO
        {
            FileUrl = fileUrl,
            FileName = "sample",
            FileExtension = "txt",
            AccessGroupId = accessGroupId,
            Tags = [],
            Categories = [],
            Description = string.Empty,
            PublicViewing = false,
            PublicDownload = false
        };
    }

    private static FileIngestionService CreateService(
        BaseDataContext context,
        MetadataStore metadataStore,
        IHttpClientFactory httpClientFactory,
        SessionDTO? session = null,
        HostAddressResolver? hostAddressResolver = null,
        long maximumFileSizeBytes = FileSizePolicy.MaximumFileSizeBytes)
    {
        var fileManager = new FileManager(metadataStore, NullLogger<FileManager>.Instance);
        var serviceLogger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var persistenceService = new FilePersistenceService(
            context,
            fileManager,
            new TestUserSessionService(session),
            serviceLogger,
            NullLogger<FilePersistenceService>.Instance,
            maximumFileSizeBytes);

        hostAddressResolver ??= (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });

        return new FileIngestionService(persistenceService, httpClientFactory, hostAddressResolver);
    }

    private static (UserDB User, AccessGroupDB AccessGroup) SeedUserWithAccessGroup(BaseDataContext context)
    {
        var user = new UserDB
        {
            Id = Guid.NewGuid(),
            Username = "url-import-user",
            PasswordHash = [1],
            PasswordSalt = [2],
            IsAdmin = false,
            IsBanned = false,
            IsWhitelisted = true,
            CreatedAt = DateTime.UtcNow
        };
        var accessGroup = new AccessGroupDB
        {
            Id = Guid.NewGuid(),
            OwnerId = user.Id,
            Owner = user,
            CreatedAt = DateTime.UtcNow,
            Name = "URL Imports",
            Users = [],
            SubGroups = []
        };

        user.AccessGroups.Add(accessGroup);
        accessGroup.Users.Add(user);
        context.Users.Add(user);
        context.AccessGroups.Add(accessGroup);
        context.SaveChanges();

        return (user, accessGroup);
    }

    private static SessionDTO CreateSession(Guid userId)
    {
        return new SessionDTO
        {
            LoggedIn = true,
            UserId = userId,
            Username = "url-import-user",
            IsWhitelisted = true,
            Roles = ["Whitelisted"],
            AccessGroups = []
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

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler? _handler;

        public RecordingHttpClientFactory(HttpMessageHandler? handler = null)
        {
            _handler = handler;
        }

        public bool WasCalled { get; private set; }
        public string? ClientName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            WasCalled = true;
            ClientName = name;
            return _handler is null
                ? new HttpClient()
                : new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _createResponse;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> createResponse)
        {
            _createResponse = createResponse;
        }

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(_createResponse(request));
        }
    }

    private sealed class DeclaredLengthContent : HttpContent
    {
        private readonly long _contentLength;

        public DeclaredLengthContent(long contentLength)
        {
            _contentLength = contentLength;
        }

        public bool SerializeWasCalled { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            SerializeWasCalled = true;
            return Task.CompletedTask;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _contentLength;
            return true;
        }
    }

    private sealed class RecordingFormFile : IFormFile
    {
        public RecordingFormFile(long length)
        {
            Length = length;
        }

        public bool OpenReadStreamWasCalled { get; private set; }
        public string ContentType { get; set; } = "text/plain";
        public string ContentDisposition { get; set; } = string.Empty;
        public IHeaderDictionary Headers { get; set; } = null!;
        public long Length { get; }
        public string Name { get; } = "FormFile";
        public string FileName { get; } = "large.txt";

        public void CopyTo(Stream target)
        {
            throw new NotSupportedException();
        }

        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Stream OpenReadStream()
        {
            OpenReadStreamWasCalled = true;
            return Stream.Null;
        }
    }

    private sealed class CountingRepeatingReadStream : Stream
    {
        private readonly byte _value;

        public CountingRepeatingReadStream(byte value, long totalLength)
        {
            _value = value;
            TotalLength = totalLength;
        }

        public long TotalLength { get; }

        public long BytesRead { get; private set; }

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
            return ReadCore(buffer.AsSpan(offset, count));
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled<int>(cancellationToken);

            return ValueTask.FromResult(ReadCore(buffer.Span));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int ReadCore(Span<byte> buffer)
        {
            if (BytesRead >= TotalLength)
                return 0;

            int bytesToRead = (int)Math.Min(buffer.Length, TotalLength - BytesRead);
            buffer[..bytesToRead].Fill(_value);
            BytesRead += bytesToRead;
            return bytesToRead;
        }
    }

    private sealed class TestUserSessionService(SessionDTO? session = null) : IUserSessionService
    {
        public Task<UserDTO> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<UserDTO>> GetAllUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsSessionUserAdminAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<UserDTO> GetSessionUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid> GetSessionUserIdAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<UserDB> GetSessionUserDBAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SessionDTO> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(session ?? new SessionDTO
            {
                LoggedIn = false,
                UserId = Guid.Empty,
                Username = string.Empty,
                AccessGroups = []
            });
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
