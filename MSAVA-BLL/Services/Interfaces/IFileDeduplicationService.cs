using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Interfaces;

/// <summary>
/// Service for checking file hashes and creating references to existing files.
/// </summary>
public interface IFileDeduplicationService
{
    /// <summary>
    /// Checks if a file with the given hash exists and returns/creates a reference.
    /// </summary>
    /// <param name="request">The hash check request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating if file exists and the reference ID.</returns>
    Task<HashCheckResult> CheckAndGetReferenceAsync(HashCheckRequest? request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch check multiple hashes and return/create references.
    /// </summary>
    Task<List<HashCheckResult>> CheckAndGetReferenceBatchAsync(List<HashCheckRequest>? requests, CancellationToken cancellationToken = default);
}
