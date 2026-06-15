using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSAVA_Shared.Models;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace MSAVA_API.Controllers;

[Route("api/files/store")]
[ApiController]
[Authorize]
public class FilesStoreController : ControllerBase
{
    private readonly IFileIngestionService _ingestionService;

    public FilesStoreController(IFileIngestionService ingestionService)
    {
        _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
    }


    [HttpPost("stream")]
    [Consumes("application/octet-stream")]
    [RequestSizeLimit(FileSizePolicy.MaximumFileSizeBytes)]
    public async Task<ActionResult<Guid>> CreateFileFromStream(
        [FromQuery][Required] string fileName,
        [FromQuery][Required] string fileExtension,
        [FromQuery][Required] List<string> tags,
        [FromQuery][Required] List<string> categories,
        [FromQuery][Required] Guid accessGroup,
        [FromQuery][Required] string description,
        [FromQuery][Required] bool publicViewing,
        [FromQuery][Required] bool publicDownload,
        CancellationToken cancellationToken = default)
    {
        var dto = new SaveFileFromStreamDTO
        {
            FileName = fileName,
            FileExtension = fileExtension,
            Tags = tags,
            Categories = categories,
            AccessGroupId = accessGroup,
            Description = description,
            PublicViewing = publicViewing,
            PublicDownload = publicDownload,
            Stream = Request.Body
        };

        var id = await _ingestionService.CreateFileFromStreamAsync(dto, cancellationToken);
        return Ok(id);
    }

    [HttpPost("url")]
    public async Task<ActionResult<Guid>> CreateFileFromUrl(
        [FromForm][Required] SaveFileFromUrlDTO dto,
        CancellationToken cancellationToken = default)
    {
        var id = await _ingestionService.CreateFileFromUrlAsync(dto, cancellationToken);
        return Ok(id);
    }

    [HttpPost("formfile")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(FileSizePolicy.MaximumFileSizeBytes)]
    public async Task<ActionResult<Guid>> CreateFileFromFormFile(
        [FromForm][Required] SaveFileFromFormFileDTO dto,
        CancellationToken cancellationToken = default)
    {
        var id = await _ingestionService.CreateFileFromFormFileAsync(dto, cancellationToken);
        return Ok(id);
    }
}
