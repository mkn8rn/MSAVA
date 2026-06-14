namespace MSAVA_BLL.Services.Interfaces;

public interface IAccessGroupService
{
    Guid CreateAccessGroup(string name);
    Task AddUserToAccessGroupAsync(Guid userId, Guid accessGroupId, CancellationToken cancellationToken = default);
}
