using Microsoft.AspNetCore.Mvc;
using MSAVA_API.Controllers;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class FilesImportControllerTests
{
    [Test]
    public void Constructor_RejectsMissingYouTubeImportService()
    {
        Action act = () => _ = new FilesImportController(
            null!,
            new TestFileImportService<FetchFileGoogleDriveDTO>(),
            new TestFileImportService<FetchFileFromOneDriveDTO>());

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("youTubeImportService");
    }

    [Test]
    public void Constructor_RejectsMissingGoogleDriveImportService()
    {
        Action act = () => _ = new FilesImportController(
            new TestFileImportService<FetchFileYouTubeDTO>(),
            null!,
            new TestFileImportService<FetchFileFromOneDriveDTO>());

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("googleDriveImportService");
    }

    [Test]
    public void Constructor_RejectsMissingOneDriveImportService()
    {
        Action act = () => _ = new FilesImportController(
            new TestFileImportService<FetchFileYouTubeDTO>(),
            new TestFileImportService<FetchFileGoogleDriveDTO>(),
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("oneDriveImportService");
    }

    [Test]
    public async Task ImportFromYouTube_ReturnsCreatedFileIdAndPassesCancellationToken()
    {
        var youtubeService = new TestFileImportService<FetchFileYouTubeDTO>();
        var controller = new FilesImportController(
            youtubeService,
            new TestFileImportService<FetchFileGoogleDriveDTO>(),
            new TestFileImportService<FetchFileFromOneDriveDTO>());
        var request = new FetchFileYouTubeDTO
        {
            YouTubeUrl = "https://example.test/watch?v=test",
            AccessGroupId = Guid.NewGuid(),
            DownloadVideo = true
        };
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.ImportFromYouTube(request, cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(youtubeService.FileId);
        youtubeService.Request.Should().BeSameAs(request);
        youtubeService.CancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task ImportFromGoogleDrive_ReturnsCreatedFileIdAndPassesCancellationToken()
    {
        var googleDriveService = new TestFileImportService<FetchFileGoogleDriveDTO>();
        var controller = new FilesImportController(
            new TestFileImportService<FetchFileYouTubeDTO>(),
            googleDriveService,
            new TestFileImportService<FetchFileFromOneDriveDTO>());
        var request = new FetchFileGoogleDriveDTO
        {
            FileUrl = "https://drive.google.com/file/d/test/view",
            AccessGroupId = Guid.NewGuid()
        };
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.ImportFromGoogleDrive(request, cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(googleDriveService.FileId);
        googleDriveService.Request.Should().BeSameAs(request);
        googleDriveService.CancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task ImportFromOneDrive_ReturnsCreatedFileIdAndPassesCancellationToken()
    {
        var oneDriveService = new TestFileImportService<FetchFileFromOneDriveDTO>();
        var controller = new FilesImportController(
            new TestFileImportService<FetchFileYouTubeDTO>(),
            new TestFileImportService<FetchFileGoogleDriveDTO>(),
            oneDriveService);
        var request = new FetchFileFromOneDriveDTO
        {
            FileUrl = "https://onedrive.live.com/example",
            AccessGroupId = Guid.NewGuid()
        };
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.ImportFromOneDrive(request, cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(oneDriveService.FileId);
        oneDriveService.Request.Should().BeSameAs(request);
        oneDriveService.CancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    private sealed class TestFileImportService<TRequest> : IFileImportService<TRequest>
    {
        public Guid FileId { get; } = Guid.NewGuid();
        public TRequest? Request { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<Guid> ImportAsync(TRequest dto, CancellationToken cancellationToken = default)
        {
            Request = dto;
            CancellationToken = cancellationToken;
            return Task.FromResult(FileId);
        }
    }
}
