using Microsoft.AspNetCore.Mvc;
using MSAVA_API.Controllers;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class FilesRetrieveControllerTests
{
    [Test]
    public void Constructor_RejectsMissingDownloadService()
    {
        Action act = () => _ = new FilesRetrieveController(null!, new TestFileQueryService());

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("downloadService");
    }

    [Test]
    public void Constructor_RejectsMissingQueryService()
    {
        Action act = () => _ = new FilesRetrieveController(new TestFileDownloadService(), null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("queryService");
    }

    [Test]
    public async Task GetFileStreamById_ReturnsFileStreamResultAndPassesCancellationToken()
    {
        var downloadService = new TestFileDownloadService();
        var controller = new FilesRetrieveController(downloadService, new TestFileQueryService());
        var refId = Guid.NewGuid();
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await controller.GetFileStreamById(refId, cancellationTokenSource.Token);

        result.FileStream.Should().BeSameAs(downloadService.StreamById.FileStream);
        result.FileDownloadName.Should().Be("stream-by-id.txt");
        result.ContentType.Should().Be("application/octet-stream");
        downloadService.StreamByIdRequest.Should().Be(refId);
        downloadService.StreamByIdCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task GetPhysicalFileByPath_ReturnsRangeEnabledPhysicalFileResultAndPassesCancellationToken()
    {
        var downloadService = new TestFileDownloadService();
        var controller = new FilesRetrieveController(downloadService, new TestFileQueryService());
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await controller.GetPhysicalFileByPath("file.txt", cancellationTokenSource.Token);

        result.FileName.Should().Be("C:\\data\\file.txt");
        result.ContentType.Should().Be("text/plain");
        result.FileDownloadName.Should().Be("file.txt");
        result.EnableRangeProcessing.Should().BeTrue();
        downloadService.PhysicalByPathRequest.Should().Be("file.txt");
        downloadService.PhysicalByPathCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task GetFileStreamByPath_ReturnsFileStreamResultAndPassesCancellationToken()
    {
        var downloadService = new TestFileDownloadService();
        var controller = new FilesRetrieveController(downloadService, new TestFileQueryService());
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await controller.GetFileStreamByPath("path-file.txt", cancellationTokenSource.Token);

        result.FileStream.Should().BeSameAs(downloadService.StreamByPath.FileStream);
        result.FileDownloadName.Should().Be("stream-by-path.txt");
        result.ContentType.Should().Be("application/octet-stream");
        downloadService.StreamByPathRequest.Should().Be("path-file.txt");
        downloadService.StreamByPathCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task GetPhysicalFileReturnDataById_ReturnsRangeEnabledPhysicalFileResultAndPassesCancellationToken()
    {
        var downloadService = new TestFileDownloadService();
        var controller = new FilesRetrieveController(downloadService, new TestFileQueryService());
        var refId = Guid.NewGuid();
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await controller.GetPhysicalFileReturnDataById(refId, cancellationTokenSource.Token);

        result.FileName.Should().Be("C:\\data\\id-file.txt");
        result.ContentType.Should().Be("text/plain");
        result.FileDownloadName.Should().Be("id-file.txt");
        result.EnableRangeProcessing.Should().BeTrue();
        downloadService.PhysicalByIdRequest.Should().Be(refId);
        downloadService.PhysicalByIdCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task GetAllFileMetadata_ReturnsServiceResultAndPassesCancellationToken()
    {
        var queryService = new TestFileQueryService();
        var controller = new FilesRetrieveController(new TestFileDownloadService(), queryService);
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.GetAllFileMetadata(cancellationToken: cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(queryService.AllMetadata);
        queryService.AllMetadataSkip.Should().Be(0);
        queryService.AllMetadataTake.Should().Be(FileQueryPagePolicy.DefaultPageSize);
        queryService.AllMetadataCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task GetAllFileMetadata_PassesPagingParameters()
    {
        var queryService = new TestFileQueryService();
        var controller = new FilesRetrieveController(new TestFileDownloadService(), queryService);
        using var cancellationTokenSource = new CancellationTokenSource();

        await controller.GetAllFileMetadata(
            skip: 20,
            take: 30,
            cancellationToken: cancellationTokenSource.Token);

        queryService.AllMetadataSkip.Should().Be(20);
        queryService.AllMetadataTake.Should().Be(30);
        queryService.AllMetadataCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task SearchFileGuidsByAllFields_PassesPagingParameters()
    {
        var queryService = new TestFileQueryService();
        var controller = new FilesRetrieveController(new TestFileDownloadService(), queryService);
        using var cancellationTokenSource = new CancellationTokenSource();

        await controller.SearchFileGuidsByAllFields(
            tag: "tag",
            category: "category",
            name: "name",
            description: "description",
            skip: 3,
            take: 4,
            cancellationToken: cancellationTokenSource.Token);

        queryService.GuidSearch.Should().Be(("tag", "category", "name", "description", 3, 4));
        queryService.GuidSearchCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    private sealed class TestFileDownloadService : IFileDownloadService
    {
        public readonly StreamReturnFileDTO StreamById = new()
        {
            FileName = "stream-by-id",
            FileExtension = "txt",
            FileStream = new MemoryStream([1, 2, 3])
        };

        public readonly StreamReturnFileDTO StreamByPath = new()
        {
            FileName = "stream-by-path",
            FileExtension = "txt",
            FileStream = new MemoryStream([4, 5, 6])
        };

        public Guid StreamByIdRequest { get; private set; }
        public CancellationToken StreamByIdCancellationToken { get; private set; }
        public string? StreamByPathRequest { get; private set; }
        public CancellationToken StreamByPathCancellationToken { get; private set; }
        public Guid PhysicalByIdRequest { get; private set; }
        public CancellationToken PhysicalByIdCancellationToken { get; private set; }
        public string? PhysicalByPathRequest { get; private set; }
        public CancellationToken PhysicalByPathCancellationToken { get; private set; }

        public Task<StreamReturnFileDTO> GetFileStreamByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            StreamByIdRequest = id;
            StreamByIdCancellationToken = cancellationToken;
            return Task.FromResult(StreamById);
        }

        public Task<StreamReturnFileDTO> GetFileStreamByPathAsync(
            string fileNameWithExtension,
            CancellationToken cancellationToken = default)
        {
            StreamByPathRequest = fileNameWithExtension;
            StreamByPathCancellationToken = cancellationToken;
            return Task.FromResult(StreamByPath);
        }

        public Task<PhysicalReturnFileDTO> GetPhysicalFileReturnDataByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            PhysicalByIdRequest = id;
            PhysicalByIdCancellationToken = cancellationToken;
            return Task.FromResult(new PhysicalReturnFileDTO
            {
                FilePath = "C:\\data\\id-file.txt",
                ContentType = "text/plain",
                FileName = "id-file.txt"
            });
        }

        public Task<PhysicalReturnFileDTO> GetPhysicalFileReturnDataByPathAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            PhysicalByPathRequest = path;
            PhysicalByPathCancellationToken = cancellationToken;
            return Task.FromResult(new PhysicalReturnFileDTO
            {
                FilePath = "C:\\data\\file.txt",
                ContentType = "text/plain",
                FileName = "file.txt"
            });
        }
    }

    private sealed class TestFileQueryService : IFileQueryService
    {
        public List<SearchFileDataDTO> AllMetadata { get; } =
        [
            new SearchFileDataDTO
            {
                DataId = Guid.NewGuid(),
                RefId = Guid.NewGuid(),
                FilePath = "file.txt",
                Name = "file",
                Description = "description",
                MimeType = "text/plain",
                FileExtension = "txt",
                Tags = ["tag"],
                Categories = ["category"],
                SizeInBytes = 10,
                Checksum = "checksum",
                PublicViewing = true,
                DownloadCount = 0,
                SavedAt = DateTime.UtcNow,
                LastModifiedAt = DateTime.UtcNow,
                LastModifiedById = Guid.NewGuid()
            }
        ];

        public CancellationToken AllMetadataCancellationToken { get; private set; }
        public int AllMetadataSkip { get; private set; }
        public int AllMetadataTake { get; private set; }
        public (string? Tag, string? Category, string? Name, string? Description, int Skip, int Take) GuidSearch { get; private set; }
        public CancellationToken GuidSearchCancellationToken { get; private set; }

        public Task<List<Guid>> GetFileGuidsByAllFieldsAsync(
            string? tag,
            string? category,
            string? name,
            string? description,
            int skip = 0,
            int take = FileQueryPagePolicy.DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            GuidSearch = (tag, category, name, description, skip, take);
            GuidSearchCancellationToken = cancellationToken;
            return Task.FromResult(new List<Guid>());
        }

        public Task<List<SearchFileDataDTO>> GetFileDataByAllFieldsAsync(
            string? tag,
            string? category,
            string? name,
            string? description,
            int skip = 0,
            int take = FileQueryPagePolicy.DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<SearchFileDataDTO>());
        }

        public Task<List<Guid>> GetAllFileGuidsAsync(
            int skip = 0,
            int take = FileQueryPagePolicy.DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<Guid>());
        }

        public Task<List<SearchFileDataDTO>> GetAllFileMetadataAsync(
            int skip = 0,
            int take = FileQueryPagePolicy.DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            AllMetadataSkip = skip;
            AllMetadataTake = take;
            AllMetadataCancellationToken = cancellationToken;
            return Task.FromResult(AllMetadata);
        }
    }
}
