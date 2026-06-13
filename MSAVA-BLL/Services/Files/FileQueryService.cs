using MSAVA_Shared.Models;
using MSAVA_BLL.Utils;
using MSAVA_INF.Models;
using MSAVA_INF.Contexts;
using MSAVA_BLL.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MSAVA_BLL.Services.Files;

public class FileQueryService : IFileQueryService
{
    private readonly BaseDataContext _context;
    private readonly IUserSessionService _userService;

    public FileQueryService(BaseDataContext context, IUserSessionService userService)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
    }

    public async Task<List<Guid>> GetAllFileGuidsAsync(CancellationToken cancellationToken = default)
    {
        var session = GetActiveSession();

        return await GetVisibleFileDataQuery(session)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SearchFileDataDTO>> GetAllFileMetadataAsync(CancellationToken cancellationToken = default)
    {
        var session = GetActiveSession();

        var dbList = await GetVisibleFileDataQuery(session)
            .Include(f => f.FileReference)
            .ToListAsync(cancellationToken);

        return dbList.Select(MappingUtils.MapSearchFileDataDTO).ToList();
    }

    public async Task<List<Guid>> GetFileGuidsByAllFieldsAsync(
        string? tag,
        string? category,
        string? name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var session = GetActiveSession();

        var query = GetVisibleFileDataQuery(session);

        query = ApplySearchFilters(query, tag, category, name, description);

        return await query.Select(f => f.Id).ToListAsync(cancellationToken);
    }

    public async Task<List<SearchFileDataDTO>> GetFileDataByAllFieldsAsync(
        string? tag,
        string? category,
        string? name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var session = GetActiveSession();

        var query = GetVisibleFileDataQuery(session)
            .Include(f => f.FileReference)
            .AsQueryable();

        query = ApplySearchFilters(query, tag, category, name, description);

        var dbList = await query.ToListAsync(cancellationToken);
        return dbList.Select(MappingUtils.MapSearchFileDataDTO).ToList();
    }

    private SessionDTO GetActiveSession()
    {
        return SessionGuard.RequireActive(
            _userService.GetSessionClaims(),
            "Session user is required to query files.",
            "Banned users cannot query files.");
    }

    private IQueryable<SavedFileDataDB> GetVisibleFileDataQuery(SessionDTO session)
    {
        var query = _context.FileData
            .AsNoTracking()
            .Where(fileData => fileData.FileReference != null);

        if (session.IsAdmin)
            return query;

        var groupIds = session.AccessGroups ?? [];
        return query.Where(fileData =>
            fileData.PublicViewing ||
            groupIds.Contains(fileData.FileReference!.AccessGroupId));
    }

    private static IQueryable<SavedFileDataDB> ApplySearchFilters(
        IQueryable<SavedFileDataDB> query,
        string? tag,
        string? category,
        string? name,
        string? description)
    {
        // Use EF.Functions.ILike for PostgreSQL case-insensitive search
        // These translate directly to SQL ILIKE operations
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var tagLower = tag.ToLowerInvariant();
            query = query.Where(f => f.Tags != null && f.Tags.Any(t => EF.Functions.ILike(t, tagLower)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var categoryLower = category.ToLowerInvariant();
            query = query.Where(f => f.Categories != null && f.Categories.Any(c => EF.Functions.ILike(c, categoryLower)));
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var namePattern = $"%{name}%";
            query = query.Where(f => f.Name != null && EF.Functions.ILike(f.Name, namePattern));
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            var descPattern = $"%{description}%";
            query = query.Where(f => f.Description != null && EF.Functions.ILike(f.Description, descPattern));
        }

        return query;
    }
}
