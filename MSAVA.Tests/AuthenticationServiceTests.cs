using System.Reflection;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Auth;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Utils;
using MSAVA_INF.Contexts;
using MSAVA_INF.Environment;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;
using Serilog.Events;

namespace MSAVA_App.Tests;

public class AuthenticationServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 16, 11, 15, 0, TimeSpan.Zero);

    [Test]
    public void AuthenticationService_DoesNotExposePublicJwtMintingMethod()
    {
        typeof(AuthenticationService)
            .GetMethod("GenerateJwtTokenAsync", BindingFlags.Instance | BindingFlags.Public)
            .Should()
            .BeNull();
    }

    [Test]
    public void JwtTokenStringModelConfiguration_UsesAuthInputPolicyLengthLimit()
    {
        using var context = CreateContext();

        var tokenProperty = context.Model
            .FindEntityType(typeof(JwtDB))
            ?.FindProperty(nameof(JwtDB.TokenString));

        tokenProperty.Should().NotBeNull();
        tokenProperty!.GetMaxLength().Should().Be(AuthInputPolicy.MaximumTokenStringLength);
        AuthInputPolicy.MaximumTokenStringLength.Should().Be(JwtDB.MaximumTokenStringLength);
        AuthInputPolicy.MaximumTokenStringLength.Should().Be(AuthenticationTokenPolicy.MaximumTokenStringLength);
    }

    [Test]
    public void AuthenticationService_DependsOnInviteCodeServiceInterface()
    {
        var constructor = typeof(AuthenticationService).GetConstructors().Should().ContainSingle().Subject;

        constructor.GetParameters()
            .Should()
            .Contain(parameter => parameter.ParameterType == typeof(IInviteCodeService));
    }

    [Test]
    public void Constructor_RejectsMissingEnvironment()
    {
        using var context = CreateContext();
        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var admin = CreateUser("admin", "password", isBanned: false);
        var inviteCodeService = new InviteCodeService(
            context,
            new TestUserSessionService(admin.Id),
            logger);

        Action act = () => _ = new AuthenticationService(context, inviteCodeService, null!, logger);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("env");
    }

    [Test]
    public async Task LoginAsync_RejectsBannedUserBeforeJwtIsPersisted()
    {
        using var context = CreateContext();
        var user = CreateUser("banned-user", "correct-password", isBanned: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = user.Username,
            Password = "correct-password"
        });

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Banned users cannot log in.");
        context.Jwts.Should().BeEmpty();
    }

    [Test]
    public async Task LoginAsync_RejectsNonWhitelistedUserBeforeJwtIsPersisted()
    {
        using var context = CreateContext();
        var user = CreateUser("pending-user", "correct-password", isBanned: false, isWhitelisted: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = user.Username,
            Password = "correct-password"
        });

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Users must be whitelisted before logging in.");
        context.Jwts.Should().BeEmpty();
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task LoginAsync_RejectsMissingUsername(string username)
    {
        using var context = CreateContext();
        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = username,
            Password = "password"
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Username must be provided.*");
        context.Jwts.Should().BeEmpty();
    }

    [TestCase("bad\nuser")]
    [TestCase("bad\u0000user")]
    public async Task LoginAsync_RejectsUsernameWithControlCharacterBeforeJwtIsPersisted(string username)
    {
        using var context = CreateContext();
        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = username,
            Password = "password"
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Username contains invalid characters.*");
        context.Jwts.Should().BeEmpty();
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task LoginAsync_RejectsMissingPassword(string password)
    {
        using var context = CreateContext();
        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = "user",
            Password = password
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Password must be provided.*");
        context.Jwts.Should().BeEmpty();
    }

    [Test]
    public async Task LoginAsync_RejectsOversizePasswordBeforeHashing()
    {
        using var context = CreateContext();
        var user = CreateUser("existing-user", "correct-password", isBanned: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = user.Username,
            Password = new string('p', AuthInputPolicy.MaximumPasswordLength + 1)
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Password must be {AuthInputPolicy.MaximumPasswordLength} characters or fewer.*");
        context.Jwts.Should().BeEmpty();
    }

    [Test]
    public async Task LoginAsync_RejectsUnknownUsernameAsUnauthorized()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = "missing-user",
            Password = "password"
        });

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Username doesn't exist or password is incorrect.");
        context.Jwts.Should().BeEmpty();
    }

    [Test]
    public async Task LoginAsync_RejectsWrongPasswordAsUnauthorized()
    {
        using var context = CreateContext();
        var user = CreateUser("existing-user", "correct-password", isBanned: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = user.Username,
            Password = "wrong-password"
        });

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Username doesn't exist or password is incorrect.");
        context.Jwts.Should().BeEmpty();
    }

    [Test]
    public async Task LoginAsync_RejectsDuplicateUsernameBeforeJwtIsPersisted()
    {
        using var context = CreateContext();
        context.Users.AddRange(
            CreateUser("duplicate-user", "correct-password", isBanned: false),
            CreateUser("duplicate-user", "other-password", isBanned: false));
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = "  duplicate-user  ",
            Password = "correct-password"
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Duplicate user records were found for the requested username.");
        context.Jwts.Should().BeEmpty();
    }

    [Test]
    public async Task LoginAsync_RejectsDuplicateUsernameIgnoringCaseBeforeJwtIsPersisted()
    {
        using var context = CreateContext();
        context.Users.AddRange(
            CreateUser("duplicate-user", "correct-password", isBanned: false),
            CreateUser("DUPLICATE-USER", "other-password", isBanned: false));
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = "  duplicate-user  ",
            Password = "correct-password"
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Duplicate user records were found for the requested username.");
        context.Jwts.Should().BeEmpty();
    }

    [Test]
    public async Task LoginAsync_TrimsUsernameBeforeLookup()
    {
        using var context = CreateContext();
        var user = CreateUser("trimmed-user", "correct-password", isBanned: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var response = await service.LoginAsync(new LoginRequestDTO
        {
            Username = "  trimmed-user  ",
            Password = "correct-password"
        });

        response.Token.Should().NotBeNullOrWhiteSpace();
        context.Jwts.Should().ContainSingle(jwt => jwt.Username == "trimmed-user");
    }

    [Test]
    public async Task LoginAsync_MatchesUsernameIgnoringCase()
    {
        using var context = CreateContext();
        var user = CreateUser("MixedCaseUser", "correct-password", isBanned: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var response = await service.LoginAsync(new LoginRequestDTO
        {
            Username = "  mixedcaseuser  ",
            Password = "correct-password"
        });

        response.Token.Should().NotBeNullOrWhiteSpace();
        context.Jwts.Should().ContainSingle(jwt => jwt.Username == "MixedCaseUser");
    }

    [Test]
    public async Task LoginAsync_PersistsInviteCodeOnJwtRecord()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        var user = CreateUser("invited-user", "correct-password", isBanned: false);
        user.InviteCodeId = inviteCode.Id;
        var accessGroup = CreateAccessGroup(user, "invited-files");
        context.InviteCodes.Add(inviteCode);
        context.Users.Add(user);
        context.AccessGroups.Add(accessGroup);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var response = await service.LoginAsync(new LoginRequestDTO
        {
            Username = "invited-user",
            Password = "correct-password"
        });

        response.Token.Should().NotBeNullOrWhiteSpace();
        context.Jwts.Should().ContainSingle(jwt =>
            jwt.UserId == user.Id &&
            jwt.Username == "invited-user" &&
            jwt.InviteCode == inviteCode.Id);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);
        token.Claims.Single(claim => claim.Type == SessionClaimNames.InviteCode)
            .Value
            .Should()
            .Be(inviteCode.Id.ToString());
        token.Claims.Single(claim => claim.Type == SessionClaimNames.AccessGroups)
            .Value
            .Should()
            .Be(accessGroup.Id.ToString());
    }

    [Test]
    public async Task LoginAsync_PersistsJwtWithInjectedClock()
    {
        using var context = CreateContext();
        var user = CreateUser("clocked-user", "correct-password", isBanned: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context, new FixedTimeProvider(FixedNow));

        var response = await service.LoginAsync(new LoginRequestDTO
        {
            Username = user.Username,
            Password = "correct-password"
        });

        var jwt = context.Jwts.Should().ContainSingle().Which;
        jwt.IssuedAt.Should().Be(FixedNow.UtcDateTime);
        jwt.ExpiresAt.Should().Be(FixedNow.UtcDateTime.AddHours(2));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);
        token.ValidFrom.Should().Be(FixedNow.UtcDateTime);
        token.ValidTo.Should().Be(FixedNow.UtcDateTime.AddHours(2));
    }

    [Test]
    public async Task LogoutAsync_RemovesPersistedJwtAndWritesLogoutLog()
    {
        using var context = CreateContext();
        var user = CreateUser("logout-user", "password", isBanned: false);
        var targetJwt = CreateJwt(user, "target-token");
        var otherJwt = CreateJwt(user, "other-token");
        context.Users.Add(user);
        context.Jwts.AddRange(targetJwt, otherJwt);
        await context.SaveChangesAsync();
        var service = CreateService(context, new FixedTimeProvider(FixedNow));

        await service.LogoutAsync("target-token");

        context.Jwts.Should().NotContain(jwt => jwt.TokenString == "target-token");
        context.Jwts.Should().ContainSingle(jwt => jwt.TokenString == "other-token");
        var log = context.UserLogs.Should().ContainSingle().Which;
        log.Action.Should().Be(UserLogAction.SessionLogOut);
        log.UserId.Should().Be(user.Id);
        log.AdminId.Should().BeNull();
        log.Timestamp.Should().Be(FixedNow.UtcDateTime);
    }

    [Test]
    public async Task LogoutAsync_DoesNotWriteLogWhenTokenIsAlreadyRevoked()
    {
        using var context = CreateContext();
        var service = CreateService(context, new FixedTimeProvider(FixedNow));

        await service.LogoutAsync("missing-token");

        context.Jwts.Should().BeEmpty();
        context.UserLogs.Should().BeEmpty();
    }

    [TestCase(" target-token")]
    [TestCase("target-token ")]
    [TestCase("target-token\t")]
    [TestCase("target-token,other-token")]
    public async Task LogoutAsync_RejectsTokenStringWithInvalidCharactersBeforeLookup(string tokenString)
    {
        using var context = CreateContext();
        var user = CreateUser("logout-user", "password", isBanned: false);
        var targetJwt = CreateJwt(user, "target-token");
        context.Users.Add(user);
        context.Jwts.Add(targetJwt);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        Func<Task> act = () => service.LogoutAsync(tokenString);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"{AuthInputPolicy.InvalidTokenStringMessage}*");
        context.Jwts.Should().ContainSingle(jwt => jwt.TokenString == "target-token");
        context.UserLogs.Should().BeEmpty();
    }

    [Test]
    public async Task LogoutAsync_RejectsOversizeTokenStringBeforeLookup()
    {
        using var context = CreateContext();
        var user = CreateUser("logout-user", "password", isBanned: false);
        var targetJwt = CreateJwt(user, "target-token");
        context.Users.Add(user);
        context.Jwts.Add(targetJwt);
        await context.SaveChangesAsync();
        var service = CreateService(context);
        string tokenString = new('t', AuthInputPolicy.MaximumTokenStringLength + 1);

        Func<Task> act = () => service.LogoutAsync(tokenString);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"{AuthInputPolicy.OversizeTokenStringMessage}*");
        context.Jwts.Should().ContainSingle(jwt => jwt.TokenString == "target-token");
        context.UserLogs.Should().BeEmpty();
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task LogoutAsync_RejectsMissingTokenString(string tokenString)
    {
        using var context = CreateContext();
        var service = CreateService(context);

        Func<Task> act = () => service.LogoutAsync(tokenString);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"{AuthInputPolicy.MissingTokenStringMessage}*");
        context.Jwts.Should().BeEmpty();
        context.UserLogs.Should().BeEmpty();
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task RegisterAsync_RejectsMissingUsername(string username)
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = username,
            Password = "password",
            InviteCode = inviteCode.Id
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Username must be provided.*");
        context.Users.Should().BeEmpty();
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task RegisterAsync_RejectsMissingPassword(string password)
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "new-user",
            Password = password,
            InviteCode = inviteCode.Id
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Password must be provided.*");
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_RejectsOversizeUsernameBeforeInviteValidation()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = new string('u', AuthInputPolicy.MaximumUsernameLength + 1),
            Password = "password",
            InviteCode = inviteCode.Id
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Username must be {AuthInputPolicy.MaximumUsernameLength} characters or fewer.*");
        context.Users.Should().BeEmpty();
    }

    [TestCase("bad\nuser")]
    [TestCase("bad\u0000user")]
    public async Task RegisterAsync_RejectsUsernameWithControlCharacterBeforeInviteValidation(string username)
    {
        using var context = CreateContext();
        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = username,
            Password = "password",
            InviteCode = Guid.NewGuid()
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Username contains invalid characters.*");
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_RejectsOversizePasswordBeforeHashing()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "new-user",
            Password = new string('p', AuthInputPolicy.MaximumPasswordLength + 1),
            InviteCode = inviteCode.Id
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Password must be {AuthInputPolicy.MaximumPasswordLength} characters or fewer.*");
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_RejectsMissingInviteCodeAsBadRequestInput()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "new-user",
            Password = "password",
            InviteCode = Guid.Empty
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Invite code is required.*");
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_RejectsInvalidInviteCodeAsBadRequestInput()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "new-user",
            Password = "password",
            InviteCode = Guid.NewGuid()
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Invalid or expired invite code.*");
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_RejectsExhaustedInviteCodeBeforeCreatingUser()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        var existingUser = CreateUser("existing-user", "password", isBanned: false);
        existingUser.InviteCodeId = inviteCode.Id;
        context.InviteCodes.Add(inviteCode);
        context.Users.Add(existingUser);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "new-user",
            Password = "password",
            InviteCode = inviteCode.Id
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Invalid or expired invite code.*");
        context.Users.Should().ContainSingle(user => user.Username == "existing-user");
        context.UserLogs.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_HonorsCanceledTokenBeforeCreatingUser()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();
        var service = CreateService(context);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "new-user",
            Password = "password",
            InviteCode = inviteCode.Id
        }, cancellationTokenSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_TrimsUsernameBeforePersistingUser()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        await service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "  new-user  ",
            Password = "password",
            InviteCode = inviteCode.Id
        });

        context.Users.Should().ContainSingle(user => user.Username == "new-user");
        context.Users.Single(user => user.Username == "new-user").IsWhitelisted.Should().BeTrue();
    }

    [Test]
    public async Task RegisterAsync_PersistsUserWithInjectedClock()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid(), FixedNow.UtcDateTime.AddHours(1));
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context, new FixedTimeProvider(FixedNow));

        await service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "clocked-user",
            Password = "password",
            InviteCode = inviteCode.Id
        });

        var user = context.Users.Should().ContainSingle().Which;
        user.Username.Should().Be("clocked-user");
        user.CreatedAt.Should().Be(FixedNow.UtcDateTime);
        user.InviteCodeId.Should().Be(inviteCode.Id);
        user.IsWhitelisted.Should().BeTrue();
    }

    [Test]
    public async Task RegisterAsync_RejectsDuplicateUsernameAfterTrimming()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        var existingUser = CreateUser("existing-user", "password", isBanned: false);
        context.InviteCodes.Add(inviteCode);
        context.Users.Add(existingUser);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "  existing-user  ",
            Password = "password",
            InviteCode = inviteCode.Id
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Username already exists.");
        context.Users.Should().ContainSingle(user => user.Username == "existing-user");
    }

    [Test]
    public async Task RegisterAsync_RejectsDuplicateUsernameIgnoringCase()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        var existingUser = CreateUser("Existing-User", "password", isBanned: false);
        context.InviteCodes.Add(inviteCode);
        context.Users.Add(existingUser);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "  existing-user  ",
            Password = "password",
            InviteCode = inviteCode.Id
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Username already exists.");
        context.Users.Should().ContainSingle(user => user.Username == "Existing-User");
    }

    private static AuthenticationService CreateService(
        BaseDataContext context,
        TimeProvider? timeProvider = null)
    {
        var serviceLogger = new ServiceLogger(
            NullLogger<ServiceLogger>.Instance,
            context,
            timeProvider);
        var inviteCodeService = new InviteCodeService(
            context,
            new TestUserSessionService(Guid.NewGuid()),
            serviceLogger,
            timeProvider);

        return new AuthenticationService(
            context,
            inviteCodeService,
            new TestEnvironment(),
            serviceLogger,
            timeProvider);
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private static UserDB CreateUser(
        string username,
        string password,
        bool isBanned,
        bool isWhitelisted = true)
    {
        var salt = PasswordUtils.GenerateSalt();
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = PasswordUtils.HashPassword(password, salt),
            PasswordSalt = salt,
            IsAdmin = false,
            IsBanned = isBanned,
            IsWhitelisted = isWhitelisted,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static JwtDB CreateJwt(UserDB user, string tokenString)
    {
        return new JwtDB
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            Username = user.Username,
            IsAdmin = user.IsAdmin,
            IsBanned = user.IsBanned,
            IsWhitelisted = user.IsWhitelisted,
            InviteCode = user.InviteCodeId ?? Guid.Empty,
            TokenString = tokenString,
            IssuedAt = FixedNow.UtcDateTime,
            ExpiresAt = FixedNow.UtcDateTime.AddHours(2)
        };
    }

    private static InviteCodeDB CreateInviteCode(Guid ownerId, DateTime? expiresAt = null)
    {
        return new InviteCodeDB
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(1),
            MaxUses = 1
        };
    }

    private static AccessGroupDB CreateAccessGroup(UserDB owner, string name)
    {
        var accessGroup = new AccessGroupDB
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Owner = owner,
            CreatedAt = DateTime.UtcNow,
            Name = name,
            Users = [],
            SubGroups = []
        };

        owner.AccessGroups.Add(accessGroup);
        accessGroup.Users.Add(owner);

        return accessGroup;
    }

    private sealed class TestUserSessionService : IUserSessionService
    {
        private readonly Guid _sessionUserId;

        public TestUserSessionService(Guid sessionUserId)
        {
            _sessionUserId = sessionUserId;
        }

        public Task<UserDTO> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<UserDTO>> GetAllUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsSessionUserAdminAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<UserDTO> GetSessionUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid> GetSessionUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(_sessionUserId);

        public Task<UserDB> GetSessionUserDBAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SessionDTO> GetCurrentSessionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestEnvironment : ILocalEnvironment
    {
        private const string SigningKey = "0123456789abcdef0123456789abcdef";

        public LocalEnvironmentValues Values { get; } = new()
        {
            JwtIssuerSigningKey = SigningKey,
            JwtIssuerName = "MSAVA.Tests",
            JwtIssuerAudience = "MSAVA.Tests",
            AdminUsername = "admin",
            AdminPassword = "admin-password",
            PostgresBaseDbUser = "postgres",
            PostgresBaseDbPassword = "postgres",
            PostgresBaseDbHost = "localhost",
            PostgresBaseDbPort = 5432,
            PostgresBaseDbDbName = "msava",
            PostgresBaseDbSslMode = "Disable",
            SerilogInformationLevel = LogEventLevel.Information,
            SerilogRollingInterval = Serilog.RollingInterval.Day,
            SerilogRetainedFileCountLimit = 7,
            SerilogFileSizeLimitBytes = 1_000_000,
            SerilogRollOnFileSizeLimit = true
        };

        public byte[] GetSigningKeyBytes() => System.Text.Encoding.UTF8.GetBytes(SigningKey);
    }

    private sealed class TestDataContext : BaseDataContext
    {
        public TestDataContext(DbContextOptions<BaseDataContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SavedFileDataDB>().Ignore(fileData => fileData.Metadata);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
