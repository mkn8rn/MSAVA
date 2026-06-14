using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Interfaces;

public interface IRequestSessionAccessor
{
    SessionDTO? GetSession();
}
