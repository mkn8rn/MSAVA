using MSAVA_Shared.Models;
using MSAVA_BLL.Services.Files;

namespace MSAVA_BLL.Services.Interfaces;

public interface IFileQueryService
{
    Task<List<Guid>> GetFileGuidsByAllFieldsAsync(
        string? tag,
        string? category,
        string? name,
        string? description,
        int skip = 0,
        int take = FileQueryPagePolicy.DefaultPageSize,
        CancellationToken cancellationToken = default);

    Task<List<SearchFileDataDTO>> GetFileDataByAllFieldsAsync(
        string? tag,
        string? category,
        string? name,
        string? description,
        int skip = 0,
        int take = FileQueryPagePolicy.DefaultPageSize,
        CancellationToken cancellationToken = default);

    Task<List<Guid>> GetAllFileGuidsAsync(
        int skip = 0,
        int take = FileQueryPagePolicy.DefaultPageSize,
        CancellationToken cancellationToken = default);

    Task<List<SearchFileDataDTO>> GetAllFileMetadataAsync(
        int skip = 0,
        int take = FileQueryPagePolicy.DefaultPageSize,
        CancellationToken cancellationToken = default);
}
