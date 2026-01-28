using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Interfaces;

public interface IFileIngestionService
{
    Task<Guid> CreateFileFromStreamAsync(SaveFileFromStreamDTO dto, CancellationToken cancellationToken = default);
    Task<Guid> CreateFileFromUrlAsync(SaveFileFromUrlDTO dto, CancellationToken cancellationToken = default);
    Task<Guid> CreateFileFromFormFileAsync(SaveFileFromFormFileDTO dto, CancellationToken cancellationToken = default);
}
