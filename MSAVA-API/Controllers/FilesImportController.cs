using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSAVA_Shared.Models;
using System.ComponentModel.DataAnnotations;
using MSAVA_BLL.Services.Interfaces;

namespace MSAVA_API.Controllers;

[Route("api/files/import")]
[ApiController]
[Authorize]
public class FilesImportController : ControllerBase
{
    private readonly IFileImportService<FetchFileYouTubeDTO> _youTubeImportService;
    private readonly IFileImportService<FetchFileGoogleDriveDTO> _googleDriveImportService;
    private readonly IFileImportService<FetchFileFromOneDriveDTO> _oneDriveImportService;

    public FilesImportController(
        IFileImportService<FetchFileYouTubeDTO> youTubeImportService,
        IFileImportService<FetchFileGoogleDriveDTO> googleDriveImportService,
        IFileImportService<FetchFileFromOneDriveDTO> oneDriveImportService)
    {
        _youTubeImportService = youTubeImportService ?? throw new ArgumentNullException(nameof(youTubeImportService));
        _googleDriveImportService = googleDriveImportService ?? throw new ArgumentNullException(nameof(googleDriveImportService));
        _oneDriveImportService = oneDriveImportService ?? throw new ArgumentNullException(nameof(oneDriveImportService));
    }

    [HttpPost("youtube")]
    public async Task<ActionResult<Guid>> ImportFromYouTube(
        [FromBody][Required] FetchFileYouTubeDTO dto,
        CancellationToken cancellationToken = default)
    {
        var id = await _youTubeImportService.ImportAsync(dto, cancellationToken);
        return Ok(id);
    }

    [HttpPost("googledrive")]
    public async Task<ActionResult<Guid>> ImportFromGoogleDrive(
        [FromBody][Required] FetchFileGoogleDriveDTO dto,
        CancellationToken cancellationToken = default)
    {
        var id = await _googleDriveImportService.ImportAsync(dto, cancellationToken);
        return Ok(id);
    }

    [HttpPost("onedrive")]
    public async Task<ActionResult<Guid>> ImportFromOneDrive(
        [FromBody][Required] FetchFileFromOneDriveDTO dto,
        CancellationToken cancellationToken = default)
    {
        var id = await _oneDriveImportService.ImportAsync(dto, cancellationToken);
        return Ok(id);
    }
}
