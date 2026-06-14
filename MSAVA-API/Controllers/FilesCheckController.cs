using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSAVA_Shared.Models;
using MSAVA_BLL.Services.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace MSAVA_API.Controllers;

/// <summary>
/// Pre-upload hash checking for duplicate detection and reference creation.
/// Clients can check if a file already exists before uploading.
/// </summary>
[Route("api/files/check")]
[ApiController]
[Authorize]
public class FilesCheckController : ControllerBase
{
    private readonly IFileDeduplicationService _deduplicationService;

    public FilesCheckController(IFileDeduplicationService deduplicationService)
    {
        _deduplicationService = deduplicationService ?? throw new ArgumentNullException(nameof(deduplicationService));
    }

    /// <summary>
    /// Check if a file with the given hash exists and get/create a reference.
    /// - If user already has access: returns their existing reference
    /// - If file exists but user has no access: creates a new reference for them
    /// - If file doesn't exist: indicates upload is required
    /// </summary>
    /// <param name="request">The hash check request with optional reference metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with reference ID or upload required indication.</returns>
    /// <response code="200">Hash check completed successfully.</response>
    /// <response code="400">Invalid request.</response>
    [HttpPost("hash")]
    [ProducesResponseType(typeof(HashCheckResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HashCheckResult>> CheckHash(
        [FromBody][Required] HashCheckRequest? request,
        CancellationToken cancellationToken = default)
    {
        var result = await _deduplicationService.CheckAndGetReferenceAsync(request, cancellationToken);

        if (result.Error != null)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Check multiple hashes at once and get/create references (batch operation).
    /// Maximum 100 hashes per request.
    /// </summary>
    /// <param name="requests">List of hash check requests.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of results with reference IDs or upload required indications.</returns>
    [HttpPost("hash/batch")]
    [ProducesResponseType(typeof(List<HashCheckResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<HashCheckResult>>> CheckHashBatch(
        [FromBody][Required] List<HashCheckRequest>? requests,
        CancellationToken cancellationToken = default)
    {
        var results = await _deduplicationService.CheckAndGetReferenceBatchAsync(requests, cancellationToken);

        if (results.Any(result => result.Error != null))
            return BadRequest(results);

        return Ok(results);
    }
}
