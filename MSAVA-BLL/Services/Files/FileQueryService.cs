using MSAVA_Shared.Models;
using MSAVA_BLL.Utils;
using MSAVA_INF.Models;
using MSAVA_INF.Contexts;
using MSAVA_BLL.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace MSAVA_BLL.Services.Files;

public class FileQueryService : IFileQueryService
{
    internal const string LikeEscapeCharacter = "\\";

    private readonly BaseDataContext _context;
    private readonly IUserSessionService _userService;

    public FileQueryService(BaseDataContext context, IUserSessionService userService)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
    }

    public async Task<List<Guid>> GetAllFileGuidsAsync(
        int skip = 0,
        int take = FileQueryPagePolicy.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var session = await GetActiveSessionAsync(cancellationToken);
        var page = FileQueryPagePolicy.Normalize(skip, take);

        return await ApplyPage(GetVisibleFileDataQuery(session), page)
            .Select(f => f.FileReferenceId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SearchFileDataDTO>> GetAllFileMetadataAsync(
        int skip = 0,
        int take = FileQueryPagePolicy.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var session = await GetActiveSessionAsync(cancellationToken);
        var page = FileQueryPagePolicy.Normalize(skip, take);

        var dbList = await ApplyPage(GetVisibleFileDataQuery(session), page)
            .Include(f => f.FileReference)
            .ToListAsync(cancellationToken);

        return dbList.Select(MappingUtils.MapSearchFileDataDTO).ToList();
    }

    public async Task<List<Guid>> GetFileGuidsByAllFieldsAsync(
        string? tag,
        string? category,
        string? name,
        string? description,
        int skip = 0,
        int take = FileQueryPagePolicy.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var session = await GetActiveSessionAsync(cancellationToken);
        var page = FileQueryPagePolicy.Normalize(skip, take);

        var query = GetVisibleFileDataQuery(session);

        query = ApplySearchFilters(query, tag, category, name, description);

        return await ApplyPage(query, page)
            .Select(f => f.FileReferenceId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SearchFileDataDTO>> GetFileDataByAllFieldsAsync(
        string? tag,
        string? category,
        string? name,
        string? description,
        int skip = 0,
        int take = FileQueryPagePolicy.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var session = await GetActiveSessionAsync(cancellationToken);
        var page = FileQueryPagePolicy.Normalize(skip, take);

        var query = GetVisibleFileDataQuery(session)
            .Include(f => f.FileReference)
            .AsQueryable();

        query = ApplySearchFilters(query, tag, category, name, description);

        var dbList = await ApplyPage(query, page).ToListAsync(cancellationToken);
        return dbList.Select(MappingUtils.MapSearchFileDataDTO).ToList();
    }

    private async Task<SessionDTO> GetActiveSessionAsync(CancellationToken cancellationToken)
    {
        return SessionGuard.RequireActiveWhitelisted(
            await _userService.GetSessionClaimsAsync(cancellationToken),
            "Session user is required to query files.",
            "Banned users cannot query files.",
            "Users must be whitelisted before querying files.");
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
            var tagPattern = BuildExactLikePattern(tag);
            query = query.Where(f =>
                f.Tags != null &&
                f.Tags.Any(t => EF.Functions.ILike(t, tagPattern, LikeEscapeCharacter)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var categoryPattern = BuildExactLikePattern(category);
            query = query.Where(f =>
                f.Categories != null &&
                f.Categories.Any(c => EF.Functions.ILike(c, categoryPattern, LikeEscapeCharacter)));
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var namePattern = BuildContainsLikePattern(name);
            query = query.Where(f =>
                f.Name != null &&
                EF.Functions.ILike(f.Name, namePattern, LikeEscapeCharacter));
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            var descPattern = BuildContainsLikePattern(description);
            query = query.Where(f =>
                f.Description != null &&
                EF.Functions.ILike(f.Description, descPattern, LikeEscapeCharacter));
        }

        return query;
    }

    private static IQueryable<SavedFileDataDB> ApplyPage(
        IQueryable<SavedFileDataDB> query,
        FileQueryPage page)
    {
        return query
            .OrderByDescending(fileData => fileData.SavedAt)
            .ThenBy(fileData => fileData.Id)
            .Skip(page.Skip)
            .Take(page.Take);
    }

    internal static string BuildExactLikePattern(string value)
    {
        return EscapeLikePattern(value.Trim());
    }

    internal static string BuildContainsLikePattern(string value)
    {
        return $"%{EscapeLikePattern(value.Trim())}%";
    }

    internal static string EscapeLikePattern(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (char c in value)
        {
            if (c is '%' or '_' or '\\')
                builder.Append(LikeEscapeCharacter);

            builder.Append(c);
        }

        return builder.ToString();
    }
}
