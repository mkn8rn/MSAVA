namespace MSAVA_BLL.Services.Interfaces
{
    public interface ISeedingService
    {
        Task SeedAsync(CancellationToken cancellationToken = default);
    }
}
