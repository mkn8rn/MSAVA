using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Interfaces;

public interface IFileDownloadService
{
    StreamReturnFileDTO GetFileStreamById(Guid id);
    StreamReturnFileDTO GetFileStreamByPath(string fileNameWithExtension);
    PhysicalReturnFileDTO GetPhysicalFileReturnDataById(Guid id);
    PhysicalReturnFileDTO GetPhysicalFileReturnDataByPath(string path);
}
