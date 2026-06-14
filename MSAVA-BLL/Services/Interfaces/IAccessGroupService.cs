namespace MSAVA_BLL.Services.Interfaces;

public interface IAccessGroupService
{
    Task<Guid> CreateAccessGroupAsync(string name, CancellationToken cancellationToken = default);
    Task AddUserToAccessGroupAsync(Guid userId, Guid accessGroupId, CancellationToken cancellationToken = default);
}
