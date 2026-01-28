using MSAVA_Shared.Models;
using MSAVA_INF.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MSAVA_INF.Environment;
using MSAVA_BLL.Utils;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Loggers;
using MSAVA_INF.Contexts;

namespace MSAVA_BLL.Services.Auth;

public class AuthenticationService : IAuthenticationService
{
    private readonly BaseDataContext _context;
    private readonly InviteCodeService _inviteCodeService;
    private readonly string _jwtIssuer;
    private readonly string _jwtAudience;
    private readonly byte[] _jwtKeyBytes;
    private readonly ServiceLogger _serviceLogger;

    public AuthenticationService(
        BaseDataContext context,
        InviteCodeService inviteCodeService,
        ILocalEnvironment env,
        ServiceLogger serviceLogger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _jwtIssuer = env.Values.JwtIssuerName;
        _jwtAudience = env.Values.JwtIssuerAudience;
        _jwtKeyBytes = env.GetSigningKeyBytes();
        _inviteCodeService = inviteCodeService ?? throw new ArgumentNullException(nameof(inviteCodeService));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
    }

    public async Task<LoginResponseDTO> LoginAsync(LoginRequestDTO request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        UserDB? user = await _context.Users
            .AsNoTracking()
            .Include(u => u.AccessGroups)
            .FirstOrDefaultAsync(u => u.Username == request.Username, cancellationToken);

        if (user == null || !PasswordUtils.VerifyPassword(request.Password, user.PasswordHash, user.PasswordSalt))
            throw new InvalidOperationException("Username doesn't exist or password is incorrect.");

        JwtDB token = await GenerateJwtTokenAsync(user);
        _serviceLogger.WriteLog(UserLogAction.SessionLogIn, $"User {user.Username} logged in successfully.", user.Id, null);

        return new LoginResponseDTO { Token = token.TokenString };
    }

    public async Task<JwtDB> GenerateJwtTokenAsync(UserDB user)
    {
        Guid jwtId = Guid.NewGuid();
        DateTime issuedAt = DateTime.UtcNow;
        DateTime expiresAt = issuedAt.AddHours(2);

        List<Claim> claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username)
        ];

        if (user.IsAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        if (user.IsBanned)
            claims.Add(new Claim(ClaimTypes.Role, "Banned"));
        if (user.IsWhitelisted)
            claims.Add(new Claim(ClaimTypes.Role, "Whitelisted"));

        claims.Add(new Claim("inviteCode", user.InviteCodeId?.ToString() ?? string.Empty));

        List<Guid> accessGroupGuids = user.AccessGroups?.Select(g => g.Id).ToList() ?? [];
        claims.Add(new Claim("accessGroups", string.Join(",", accessGroupGuids)));

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
            TokenString = tokenString,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt
        };

        _context.Jwts.Add(jwtDb);
        await _context.SaveChangesAsync();

        return jwtDb;
    }

    public async Task<Guid> RegisterAsync(RegisterRequestDTO request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.InviteCode == Guid.Empty)
            throw new InvalidOperationException("Invite code is required.");

        if (!_inviteCodeService.IsValidInviteCode(request.InviteCode))
            throw new InvalidOperationException("Invalid or expired invite code.");

        bool exists = await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Username == request.Username, cancellationToken);

        if (exists)
            throw new InvalidOperationException("Username already exists.");

        byte[] salt = PasswordUtils.GenerateSalt();
        byte[] hash = PasswordUtils.HashPassword(request.Password, salt);

        var user = new UserDB
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            PasswordHash = hash,
            PasswordSalt = salt,
            IsAdmin = false,
            IsBanned = false,
            IsWhitelisted = false,
            InviteCodeId = request.InviteCode,
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        _serviceLogger.WriteLog(UserLogAction.AccountRegistered, $"User {user.Username} registered successfully.", user.Id, null);

        return user.Id;
    }
}
