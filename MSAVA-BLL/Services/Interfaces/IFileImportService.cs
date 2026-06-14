namespace MSAVA_BLL.Services.Interfaces;

public interface IFileImportService<in TRequest>
{
    Task<Guid> ImportAsync(TRequest dto, CancellationToken cancellationToken = default);
}
