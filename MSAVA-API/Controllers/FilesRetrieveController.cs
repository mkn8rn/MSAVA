using MSAVA_Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_API.Attributes;
using MSAVA_BLL.Services.Files;

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
    public async Task<FileStreamResult> GetFileStreamById(
        Guid refId,
        CancellationToken cancellationToken = default)
    {
        var dto = await _downloadService.GetFileStreamByIdAsync(refId, cancellationToken);
        return new FileStreamResult(dto.FileStream, "application/octet-stream")
        {
            FileDownloadName = dto.DownloadFileName
        };
    }

    [HttpGet("stream/{**fileNameWithExtension}")]
    public async Task<FileStreamResult> GetFileStreamByPath(
        [TaintedPathCheck] string fileNameWithExtension,
        CancellationToken cancellationToken = default)
    {
        var dto = await _downloadService.GetFileStreamByPathAsync(fileNameWithExtension, cancellationToken);
        return new FileStreamResult(dto.FileStream, "application/octet-stream")
        {
            FileDownloadName = dto.DownloadFileName
        };
    }

    [HttpGet("physical/{refId:guid}")]
    public async Task<PhysicalFileResult> GetPhysicalFileReturnDataById(
        Guid refId,
        CancellationToken cancellationToken = default)
    {
        var fileData = await _downloadService.GetPhysicalFileReturnDataByIdAsync(refId, cancellationToken);
        return PhysicalFile(fileData.FilePath, fileData.ContentType, fileData.FileName, enableRangeProcessing: true);
    }

    [HttpGet("physical/{**fileNameWithExtension}")]
    public async Task<PhysicalFileResult> GetPhysicalFileByPath(
        [TaintedPathCheck] string fileNameWithExtension,
        CancellationToken cancellationToken = default)
    {
        var fileData = await _downloadService.GetPhysicalFileReturnDataByPathAsync(fileNameWithExtension, cancellationToken);
        return PhysicalFile(fileData.FilePath, fileData.ContentType, fileData.FileName, enableRangeProcessing: true);
    }

    [HttpGet("meta/all")]
    public async Task<ActionResult<List<SearchFileDataDTO>>> GetAllFileMetadata(
        [FromQuery] int skip = 0,
        [FromQuery] int take = FileQueryPagePolicy.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _queryService.GetAllFileMetadataAsync(skip, take, cancellationToken);
        return Ok(result);
    }

    [HttpGet("meta/all/id")]
    public async Task<ActionResult<List<Guid>>> SearchFileGuidsByAllFields(
        [FromQuery] string? tag,
        [FromQuery] string? category,
        [FromQuery] string? name,
        [FromQuery] string? description,
        [FromQuery] int skip = 0,
        [FromQuery] int take = FileQueryPagePolicy.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _queryService.GetFileGuidsByAllFieldsAsync(tag, category, name, description, skip, take, cancellationToken);
        return Ok(result);
    }

    [HttpGet("meta/all/data")]
    public async Task<ActionResult<List<SearchFileDataDTO>>> SearchFilesByAllFields(
        [FromQuery] string? tag,
        [FromQuery] string? category,
        [FromQuery] string? name,
        [FromQuery] string? description,
        [FromQuery] int skip = 0,
        [FromQuery] int take = FileQueryPagePolicy.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _queryService.GetFileDataByAllFieldsAsync(tag, category, name, description, skip, take, cancellationToken);
        return Ok(result);
    }
}
