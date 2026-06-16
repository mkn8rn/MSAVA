using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using MSAVA_API.Controllers;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class FilesStoreControllerTests
{
    [Test]
    public void Constructor_RejectsMissingIngestionService()
    {
        Action act = () => _ = new FilesStoreController(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("ingestionService");
    }

    [Test]
    public void CreateFileFromFormFile_DeclaresRequestAndMultipartBodyLimits()
    {
        var method = typeof(FilesStoreController).GetMethod(nameof(FilesStoreController.CreateFileFromFormFile))
            ?? throw new InvalidOperationException("CreateFileFromFormFile action was not found.");

        method.GetCustomAttributes<RequestSizeLimitAttribute>()
            .Should()
            .ContainSingle()
            .Which
            .Should()
            .BeAssignableTo<IRequestSizeLimitMetadata>()
            .Which
            .MaxRequestBodySize
            .Should()
            .Be(FileSizePolicy.MaximumFileSizeBytes);

        method.GetCustomAttributes<RequestFormLimitsAttribute>()
            .Should()
            .ContainSingle()
            .Which
            .MultipartBodyLengthLimit
            .Should()
            .Be(FileSizePolicy.MaximumFileSizeBytes);
    }

    [Test]
    public async Task CreateFileFromStream_BuildsRequestFromQueryValuesAndRequestBody()
    {
        var service = new RecordingFileIngestionService();
        var controller = new FilesStoreController(service);
        var body = new MemoryStream([1, 2, 3]);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.Request.Body = body;
        var tags = new List<string> { "invoice", "signed" };
        var categories = new List<string> { "finance" };
        var accessGroup = Guid.NewGuid();
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.CreateFileFromStream(
            "quarterly-report",
            "pdf",
            tags,
            categories,
            accessGroup,
            "Reviewed finance report",
            publicViewing: true,
            publicDownload: false,
            cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(service.StreamFileId);
        service.StreamRequest.Should().NotBeNull();
        service.StreamRequest!.FileName.Should().Be("quarterly-report");
        service.StreamRequest.FileExtension.Should().Be("pdf");
        service.StreamRequest.Tags.Should().BeSameAs(tags);
        service.StreamRequest.Categories.Should().BeSameAs(categories);
        service.StreamRequest.AccessGroupId.Should().Be(accessGroup);
        service.StreamRequest.Description.Should().Be("Reviewed finance report");
        service.StreamRequest.PublicViewing.Should().BeTrue();
        service.StreamRequest.PublicDownload.Should().BeFalse();
        service.StreamRequest.Stream.Should().BeSameAs(body);
        service.StreamCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task CreateFileFromUrl_DelegatesRequestAndCancellationToken()
    {
        var service = new RecordingFileIngestionService();
        var controller = new FilesStoreController(service);
        var request = new SaveFileFromUrlDTO
        {
            FileUrl = "https://example.test/file.txt",
            FileName = "file",
            FileExtension = "txt",
            AccessGroupId = Guid.NewGuid(),
            Tags = ["remote"],
            Categories = ["documents"],
            Description = "Remote text file",
            PublicViewing = false,
            PublicDownload = true
        };
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.CreateFileFromUrl(request, cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(service.UrlFileId);
        service.UrlRequest.Should().BeSameAs(request);
        service.UrlCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task CreateFileFromFormFile_DelegatesRequestAndCancellationToken()
    {
        var service = new RecordingFileIngestionService();
        var controller = new FilesStoreController(service);
        await using var fileStream = new MemoryStream([4, 5, 6]);
        var request = new SaveFileFromFormFileDTO
        {
            FileName = "upload",
            FileExtension = "bin",
            FormFile = new FormFile(fileStream, 0, fileStream.Length, "file", "upload.bin"),
            AccessGroupId = Guid.NewGuid(),
            Tags = ["local"],
            Categories = ["uploads"],
            Description = "Local upload",
            PublicViewing = true,
            PublicDownload = true
        };
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.CreateFileFromFormFile(request, cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(service.FormFileId);
        service.FormFileRequest.Should().BeSameAs(request);
        service.FormFileCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    private sealed class RecordingFileIngestionService : IFileIngestionService
    {
        public Guid StreamFileId { get; } = Guid.NewGuid();
        public Guid UrlFileId { get; } = Guid.NewGuid();
        public Guid FormFileId { get; } = Guid.NewGuid();

        public SaveFileFromStreamDTO? StreamRequest { get; private set; }
        public SaveFileFromUrlDTO? UrlRequest { get; private set; }
        public SaveFileFromFormFileDTO? FormFileRequest { get; private set; }

        public CancellationToken StreamCancellationToken { get; private set; }
        public CancellationToken UrlCancellationToken { get; private set; }
        public CancellationToken FormFileCancellationToken { get; private set; }

        public Task<Guid> CreateFileFromStreamAsync(
            SaveFileFromStreamDTO dto,
            CancellationToken cancellationToken = default)
        {
            StreamRequest = dto;
            StreamCancellationToken = cancellationToken;
            return Task.FromResult(StreamFileId);
        }

        public Task<Guid> CreateFileFromUrlAsync(
            SaveFileFromUrlDTO dto,
            CancellationToken cancellationToken = default)
        {
            UrlRequest = dto;
            UrlCancellationToken = cancellationToken;
            return Task.FromResult(UrlFileId);
        }

        public Task<Guid> CreateFileFromFormFileAsync(
            SaveFileFromFormFileDTO dto,
            CancellationToken cancellationToken = default)
        {
            FormFileRequest = dto;
            FormFileCancellationToken = cancellationToken;
            return Task.FromResult(FormFileId);
        }
    }
}
