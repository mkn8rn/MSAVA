using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Utils;
using MSAVA_INF.Models;
using MSAVA_INF.Contexts;
using MSAVA_Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Transactions;

namespace MSAVA_BLL.Services.Auth;

public class AccessGroupService
    : IAccessGroupService
{
    private readonly BaseDataContext _context;
    private readonly IUserSessionService _userService;
    private readonly ServiceLogger _serviceLogger;
    private readonly TimeProvider _timeProvider;

    public AccessGroupService(
        BaseDataContext context,
        IUserSessionService userService,
        ServiceLogger serviceLogger,
        TimeProvider? timeProvider = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Guid> CreateAccessGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        string accessGroupName = AccessGroupInputPolicy.NormalizeName(name);

        if (!ShouldUseSerializableAccessGroupTransaction())
            return await CreateValidatedAccessGroupAsync(accessGroupName, cancellationToken);

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = CreateSerializableAccessGroupScope();
            Guid accessGroupId = await CreateValidatedAccessGroupAsync(accessGroupName, cancellationToken);

            transaction.Complete();
            return accessGroupId;
        });
    }

    private async Task<Guid> CreateValidatedAccessGroupAsync(
        string accessGroupName,
        CancellationToken cancellationToken)
    {
        SessionDTO session = await GetActiveSessionAsync(cancellationToken);

        var user = await _context.Users.SingleOrDefaultAsync(u => u.Id == session.UserId, cancellationToken)
            ?? throw new KeyNotFoundException($"User with id {session.UserId} not found.");

        if (await OwnerHasAccessGroupNameAsync(user.Id, accessGroupName, cancellationToken))
            throw new InvalidOperationException($"Access group '{accessGroupName}' already exists for this owner.");

        var accessGroup = new AccessGroupDB
        {
            Id = Guid.NewGuid(),
            OwnerId = user.Id,
            CreatedAt = GetUtcNow(),
            Name = accessGroupName,
            Users = [],
            SubGroups = []
        };

        _context.AccessGroups.Add(accessGroup);

        // Add user to the access group in the same transaction
        user.AccessGroups ??= [];
        user.AccessGroups.Add(accessGroup);

        await _context.SaveChangesAsync(cancellationToken);

        await _serviceLogger.WriteLogAsync(
            GroupLogActions.AccessGroupCreated,
            $"Access group '{accessGroupName}' created by user {user.Username}.",
            user.Id,
            accessGroup.Id,
            cancellationToken);
        await _serviceLogger.WriteLogAsync(
            GroupLogActions.AccessGroupUserAdded,
            $"User {user.Username} added to access group '{accessGroupName}'.",
            user.Id,
            accessGroup.Id,
            cancellationToken);

        return accessGroup.Id;
    }

    private async Task<bool> OwnerHasAccessGroupNameAsync(
        Guid ownerId,
        string accessGroupName,
        CancellationToken cancellationToken)
    {
        var ownerGroupNames = await _context.AccessGroups
            .AsNoTracking()
            .Where(group => group.OwnerId == ownerId)
            .Select(group => group.Name)
            .ToListAsync(cancellationToken);

        return ownerGroupNames.Contains(accessGroupName, StringComparer.OrdinalIgnoreCase);
    }

    public async Task AddUserToAccessGroupAsync(Guid userId, Guid accessGroupId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id must be provided.", nameof(userId));

        if (accessGroupId == Guid.Empty)
            throw new ArgumentException("Access group id must be provided.", nameof(accessGroupId));

        SessionDTO session = await GetActiveSessionAsync(cancellationToken);

        var accessGroup = await _context.AccessGroups.SingleOrDefaultAsync(g => g.Id == accessGroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Access group with id {accessGroupId} not found.");

        if (!session.IsAdmin && accessGroup.OwnerId != session.UserId)
            throw new UnauthorizedAccessException("Only admins and access group owners can add users to an access group.");

        var user = await _context.Users
            .Include(u => u.AccessGroups)
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException($"User with id {userId} not found.");

        if (user.IsBanned)
            throw new UnauthorizedAccessException("Banned users cannot be added to access groups.");

        if (!user.IsWhitelisted)
            throw new UnauthorizedAccessException("Users must be whitelisted before being added to access groups.");

        user.AccessGroups ??= [];

        if (!user.AccessGroups.Any(g => g.Id == accessGroupId))
        {
            user.AccessGroups.Add(accessGroup);
            await _context.SaveChangesAsync(cancellationToken);

            await _serviceLogger.WriteLogAsync(
                GroupLogActions.AccessGroupUserAdded,
                $"User {user.Username} added to access group '{accessGroup.Name}'.",
                session.UserId,
                accessGroup.Id,
                cancellationToken);
        }
    }

    private async Task<SessionDTO> GetActiveSessionAsync(CancellationToken cancellationToken)
    {
        return SessionGuard.RequireActiveWhitelisted(
            await _userService.GetCurrentSessionAsync(cancellationToken),
            "Session user is required to manage access groups.",
            "Banned users cannot manage access groups.",
            "Users must be whitelisted before managing access groups.");
    }

    private bool ShouldUseSerializableAccessGroupTransaction()
    {
        if (_context.Database.CurrentTransaction is not null)
            return false;

        if (Transaction.Current is not null)
            return false;

        string? providerName = _context.Database.ProviderName;
        return !string.IsNullOrWhiteSpace(providerName) &&
            !string.Equals(
                providerName,
                "Microsoft.EntityFrameworkCore.InMemory",
                StringComparison.Ordinal);
    }

    private static TransactionScope CreateSerializableAccessGroupScope()
    {
        return new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions
            {
                IsolationLevel = IsolationLevel.Serializable
            },
            TransactionScopeAsyncFlowOption.Enabled);
    }

    private DateTime GetUtcNow()
    {
        return _timeProvider.GetUtcNow().UtcDateTime;
    }

}
