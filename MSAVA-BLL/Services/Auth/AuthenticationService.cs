using MSAVA_Shared.Models;
using MSAVA_INF.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Transactions;
using MSAVA_INF.Environment;
using MSAVA_BLL.Utils;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Loggers;
using MSAVA_INF.Contexts;

namespace MSAVA_BLL.Services.Auth;

public class AuthenticationService : IAuthenticationService
{
    private readonly BaseDataContext _context;
    private readonly IInviteCodeService _inviteCodeService;
    private readonly string _jwtIssuer;
    private readonly string _jwtAudience;
    private readonly byte[] _jwtKeyBytes;
    private readonly ServiceLogger _serviceLogger;
    private readonly TimeProvider _timeProvider;

    public AuthenticationService(
        BaseDataContext context,
        IInviteCodeService inviteCodeService,
        ILocalEnvironment env,
        ServiceLogger serviceLogger,
        TimeProvider? timeProvider = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        ArgumentNullException.ThrowIfNull(env);

        _jwtIssuer = env.Values.JwtIssuerName;
        _jwtAudience = env.Values.JwtIssuerAudience;
        _jwtKeyBytes = env.GetSigningKeyBytes();
        _inviteCodeService = inviteCodeService ?? throw new ArgumentNullException(nameof(inviteCodeService));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<LoginResponseDTO> LoginAsync(LoginRequestDTO request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string username = AuthInputPolicy.NormalizeUsername(request.Username);
        AuthInputPolicy.EnsurePasswordAllowed(request.Password);

        UserDB? user = await GetUniqueUserForLoginAsync(username, cancellationToken);

        if (user == null || !PasswordUtils.VerifyPassword(request.Password, user.PasswordHash, user.PasswordSalt))
            throw new UnauthorizedAccessException("Username doesn't exist or password is incorrect.");

        if (user.IsBanned)
            throw new UnauthorizedAccessException("Banned users cannot log in.");

        if (!user.IsWhitelisted)
            throw new UnauthorizedAccessException("Users must be whitelisted before logging in.");

        JwtDB token = await GenerateJwtTokenAsync(user, cancellationToken);
        await _serviceLogger.WriteLogAsync(UserLogAction.SessionLogIn, $"User {user.Username} logged in successfully.", user.Id, null);

        return new LoginResponseDTO { Token = token.TokenString };
    }

    private async Task<UserDB?> GetUniqueUserForLoginAsync(string username, CancellationToken cancellationToken)
    {
        string usernameComparisonKey = CreateUsernameComparisonKey(username);

        List<UserDB> matchingUsers = await _context.Users
            .AsNoTracking()
            .Include(u => u.AccessGroups)
            .Where(u => u.Username.ToUpper() == usernameComparisonKey)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matchingUsers.Count > 1)
            throw new InvalidOperationException("Duplicate user records were found for the requested username.");

        return matchingUsers.SingleOrDefault();
    }

    private async Task<JwtDB> GenerateJwtTokenAsync(UserDB user, CancellationToken cancellationToken)
    {
        Guid jwtId = Guid.NewGuid();
        DateTime issuedAt = GetUtcNow();
        DateTime expiresAt = issuedAt.AddHours(2);

        List<Claim> claims =
        [
            new Claim(SessionClaimNames.Subject, user.Id.ToString()),
            new Claim(SessionClaimNames.UniqueName, user.Username)
        ];

        if (user.IsAdmin)
            claims.Add(new Claim(ClaimTypes.Role, SessionRoles.Admin));
        if (user.IsBanned)
            claims.Add(new Claim(ClaimTypes.Role, SessionRoles.Banned));
        if (user.IsWhitelisted)
            claims.Add(new Claim(ClaimTypes.Role, SessionRoles.Whitelisted));

        claims.Add(new Claim(SessionClaimNames.InviteCode, user.InviteCodeId?.ToString() ?? string.Empty));

        List<Guid> accessGroupGuids = user.AccessGroups?.Select(g => g.Id).ToList() ?? [];
        claims.Add(new Claim(SessionClaimNames.AccessGroups, string.Join(",", accessGroupGuids)));

        var key = new SymmetricSecurityKey(_jwtKeyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtIssuer,
            audience: _jwtAudience,
            claims: claims,
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: creds);

        string tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        var jwtDb = new JwtDB
        {
            Id = jwtId,
            UserId = user.Id,
            Username = user.Username,
            IsAdmin = user.IsAdmin,
            IsBanned = user.IsBanned,
            IsWhitelisted = user.IsWhitelisted,
            InviteCode = user.InviteCodeId ?? Guid.Empty,
            TokenString = tokenString,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt
        };

        _context.Jwts.Add(jwtDb);
        await _context.SaveChangesAsync(cancellationToken);

        return jwtDb;
    }

    public async Task<Guid> RegisterAsync(RegisterRequestDTO request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string username = AuthInputPolicy.NormalizeUsername(request.Username);
        AuthInputPolicy.EnsurePasswordAllowed(request.Password);

        if (request.InviteCode == Guid.Empty)
            throw new ArgumentException("Invite code is required.", nameof(RegisterRequestDTO.InviteCode));

        if (!ShouldUseSerializableRegistrationTransaction())
            return await RegisterValidatedUserAsync(username, request.Password, request.InviteCode, cancellationToken);

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = CreateSerializableRegistrationScope();
            Guid userId = await RegisterValidatedUserAsync(username, request.Password, request.InviteCode, cancellationToken);

            transaction.Complete();
            return userId;
        });
    }

    private async Task<Guid> RegisterValidatedUserAsync(
        string username,
        string password,
        Guid inviteCode,
        CancellationToken cancellationToken)
    {
        if (!await _inviteCodeService.IsValidInviteCodeAsync(inviteCode, cancellationToken))
            throw new ArgumentException("Invalid or expired invite code.", nameof(RegisterRequestDTO.InviteCode));

        if (await UsernameExistsAsync(username, cancellationToken))
            throw new InvalidOperationException("Username already exists.");

        byte[] salt = PasswordUtils.GenerateSalt();
        byte[] hash = PasswordUtils.HashPassword(password, salt);

        var user = new UserDB
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = hash,
            PasswordSalt = salt,
            IsAdmin = false,
            IsBanned = false,
            IsWhitelisted = true,
            InviteCodeId = inviteCode,
            CreatedAt = GetUtcNow()
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        await _serviceLogger.WriteLogAsync(UserLogAction.AccountRegistered, $"User {user.Username} registered successfully.", user.Id, null);

        return user.Id;
    }

    private async Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken)
    {
        string usernameComparisonKey = CreateUsernameComparisonKey(username);

        return await _context.Users
            .AsNoTracking()
            .AnyAsync(user => user.Username.ToUpper() == usernameComparisonKey, cancellationToken);
    }

    private static string CreateUsernameComparisonKey(string username)
    {
        return username.ToUpperInvariant();
    }

    private bool ShouldUseSerializableRegistrationTransaction()
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

    private static TransactionScope CreateSerializableRegistrationScope()
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
