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
    public void GetFileStreamById_ReturnsFileStreamResult()
    {
        var downloadService = new TestFileDownloadService();
        var controller = new FilesRetrieveController(downloadService, new TestFileQueryService());
        var refId = Guid.NewGuid();

        var result = controller.GetFileStreamById(refId);

        result.FileStream.Should().BeSameAs(downloadService.StreamById.FileStream);
        result.FileDownloadName.Should().Be("stream-by-id.txt");
        result.ContentType.Should().Be("application/octet-stream");
        downloadService.StreamByIdRequest.Should().Be(refId);
    }

    [Test]
    public void GetPhysicalFileByPath_ReturnsRangeEnabledPhysicalFileResult()
    {
        var downloadService = new TestFileDownloadService();
        var controller = new FilesRetrieveController(downloadService, new TestFileQueryService());

        var result = controller.GetPhysicalFileByPath("file.txt");

        result.FileName.Should().Be("C:\\data\\file.txt");
        result.ContentType.Should().Be("text/plain");
        result.FileDownloadName.Should().Be("file.txt");
        result.EnableRangeProcessing.Should().BeTrue();
        downloadService.PhysicalByPathRequest.Should().Be("file.txt");
    }

    [Test]
    public async Task GetAllFileMetadata_ReturnsServiceResultAndPassesCancellationToken()
    {
        var queryService = new TestFileQueryService();
        var controller = new FilesRetrieveController(new TestFileDownloadService(), queryService);
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.GetAllFileMetadata(cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(queryService.AllMetadata);
        queryService.AllMetadataCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    private sealed class TestFileDownloadService : IFileDownloadService
    {
        public readonly StreamReturnFileDTO StreamById = new()
        {
            FileName = "stream-by-id",
            FileExtension = "txt",
            FileStream = new MemoryStream([1, 2, 3])
        };

        public Guid StreamByIdRequest { get; private set; }
        public string? PhysicalByPathRequest { get; private set; }

        public StreamReturnFileDTO GetFileStreamById(Guid id)
        {
            StreamByIdRequest = id;
            return StreamById;
        }

        public StreamReturnFileDTO GetFileStreamByPath(string fileNameWithExtension)
        {
            return new StreamReturnFileDTO
            {
                FileName = "stream-by-path",
                FileExtension = "txt",
                FileStream = new MemoryStream([4, 5, 6])
            };
        }

        public PhysicalReturnFileDTO GetPhysicalFileReturnDataById(Guid id)
        {
            return new PhysicalReturnFileDTO
            {
                FilePath = "C:\\data\\id-file.txt",
                ContentType = "text/plain",
                FileName = "id-file.txt"
            };
        }

        public PhysicalReturnFileDTO GetPhysicalFileReturnDataByPath(string path)
        {
            PhysicalByPathRequest = path;
            return new PhysicalReturnFileDTO
            {
                FilePath = "C:\\data\\file.txt",
                ContentType = "text/plain",
                FileName = "file.txt"
            };
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

        public Task<List<Guid>> GetFileGuidsByAllFieldsAsync(
            string? tag,
            string? category,
            string? name,
            string? description,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<Guid>());
        }

        public Task<List<SearchFileDataDTO>> GetFileDataByAllFieldsAsync(
            string? tag,
            string? category,
            string? name,
            string? description,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<SearchFileDataDTO>());
        }

        public Task<List<Guid>> GetAllFileGuidsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<Guid>());
        }

        public Task<List<SearchFileDataDTO>> GetAllFileMetadataAsync(CancellationToken cancellationToken = default)
        {
            AllMetadataCancellationToken = cancellationToken;
            return Task.FromResult(AllMetadata);
        }
    }
}
