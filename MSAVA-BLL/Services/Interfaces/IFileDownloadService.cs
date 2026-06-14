using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Interfaces;

public interface IFileDownloadService
{
    Task<StreamReturnFileDTO> GetFileStreamByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<StreamReturnFileDTO> GetFileStreamByPathAsync(
        string fileNameWithExtension,
        CancellationToken cancellationToken = default);

    Task<PhysicalReturnFileDTO> GetPhysicalFileReturnDataByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PhysicalReturnFileDTO> GetPhysicalFileReturnDataByPathAsync(
        string path,
        CancellationToken cancellationToken = default);
}
