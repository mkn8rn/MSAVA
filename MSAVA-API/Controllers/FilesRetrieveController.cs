using MSAVA_Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_API.Attributes;

namespace MSAVA_API.Controllers;

[Route("api/files/retrieve")]
[ApiController]
[Authorize]
public class FilesRetrieveController : ControllerBase
{
    private readonly IFileDownloadService _downloadService;
    private readonly IFileQueryService _queryService;

    public FilesRetrieveController(IFileDownloadService downloadService, IFileQueryService queryService)
    {
        _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    [HttpGet("stream/{refId:guid}")]
    public IActionResult GetFileStreamById(Guid refId)
    {
        var dto = _downloadService.GetFileStreamById(refId);
        return new FileStreamResult(dto.FileStream, "application/octet-stream")
        {
            FileDownloadName = dto.DownloadFileName
        };
    }

    [HttpGet("stream/{**fileNameWithExtension}")]
    public IActionResult GetFileStreamByPath([TaintedPathCheck] string fileNameWithExtension)
    {
        var dto = _downloadService.GetFileStreamByPath(fileNameWithExtension);
        return new FileStreamResult(dto.FileStream, "application/octet-stream")
        {
            FileDownloadName = dto.DownloadFileName
        };
    }

    [HttpGet("physical/{refId:guid}")]
    public IActionResult GetPhysicalFileReturnDataById(Guid refId)
    {
        var fileData = _downloadService.GetPhysicalFileReturnDataById(refId);
        return PhysicalFile(fileData.FilePath, fileData.ContentType, fileData.FileName, enableRangeProcessing: true);
    }

    [HttpGet("physical/{**fileNameWithExtension}")]
    public IActionResult GetPhysicalFileByPath([TaintedPathCheck] string fileNameWithExtension)
    {
        var fileData = _downloadService.GetPhysicalFileReturnDataByPath(fileNameWithExtension);
        return PhysicalFile(fileData.FilePath, fileData.ContentType, fileData.FileName, enableRangeProcessing: true);
    }

    [HttpGet("meta/all")]
    public async Task<ActionResult<List<SearchFileDataDTO>>> GetAllFileMetadata(CancellationToken cancellationToken)
    {
        var result = await _queryService.GetAllFileMetadataAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("meta/all/id")]
    public async Task<ActionResult<List<Guid>>> SearchFileGuidsByAllFields(
        [FromQuery] string? tag,
        [FromQuery] string? category,
        [FromQuery] string? name,
        [FromQuery] string? description,
        CancellationToken cancellationToken)
    {
        var result = await _queryService.GetFileGuidsByAllFieldsAsync(tag, category, name, description, cancellationToken);
        return Ok(result);
    }

    [HttpGet("meta/all/data")]
    public async Task<ActionResult<List<SearchFileDataDTO>>> SearchFilesByAllFields(
        [FromQuery] string? tag,
        [FromQuery] string? category,
        [FromQuery] string? name,
        [FromQuery] string? description,
        CancellationToken cancellationToken)
    {
        var result = await _queryService.GetFileDataByAllFieldsAsync(tag, category, name, description, cancellationToken);
        return Ok(result);
    }
}
