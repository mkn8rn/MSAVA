using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_API.Middleware;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class ExceptionCatcherMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_ReturnsOriginalErrorResponseWhenDatabaseLoggingFails()
    {
        using var dbContext = CreateContext(throwOnSave: true);
        var context = CreateHttpContext(dbContext, isDevelopment: false);
        var middleware = new ExceptionCatcherMiddleware(_ => throw new KeyNotFoundException("File reference missing."));

        await middleware.InvokeAsync(context);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        context.Response.ContentType.Should().Be("application/json");
        response.Should().NotBeNull();
        response!.Message.Should().Be("File reference missing.");
        response.StackTrace.Should().BeNull();
        dbContext.ChangeTracker.Entries<ErrorLogDB>().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_PersistsErrorLogWithAuthenticatedUserWhenDatabaseLoggingSucceeds()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(dbContext, isDevelopment: false, userId);
        var middleware = new ExceptionCatcherMiddleware(_ => throw new ArgumentException("Bad query."));

        await middleware.InvokeAsync(context);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);
        var errorLog = dbContext.ErrorLogs.Single();

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        response.Should().NotBeNull();
        response!.UserId.Should().Be(userId);
        errorLog.Id.Should().Be(response.Id);
        errorLog.UserId.Should().Be(userId);
        errorLog.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        dbContext.SaveChangesCalls.Should().Be(0);
        dbContext.SaveChangesAsyncCalls.Should().Be(1);
    }

    [Test]
    public async Task InvokeAsync_PersistsErrorLogWithJwtSubjectWhenNameIdentifierIsMissing()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(dbContext, isDevelopment: false, userId, JwtRegisteredClaimNames.Sub);
        var middleware = new ExceptionCatcherMiddleware(_ => throw new ArgumentException("Bad query."));

        await middleware.InvokeAsync(context);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);
        var errorLog = dbContext.ErrorLogs.Single();

        response.Should().NotBeNull();
        response!.UserId.Should().Be(userId);
        errorLog.UserId.Should().Be(userId);
    }

    [Test]
    public async Task InvokeAsync_ReturnsUnauthorizedForAnonymousUnauthorizedAccess()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var context = CreateHttpContext(dbContext, isDevelopment: false);
        var middleware = new ExceptionCatcherMiddleware(_ => throw new UnauthorizedAccessException("Session user is required."));

        await middleware.InvokeAsync(context);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);
        var errorLog = dbContext.ErrorLogs.Single();

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        response.Should().NotBeNull();
        response!.UserId.Should().BeNull();
        errorLog.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        errorLog.UserId.Should().BeNull();
    }

    [Test]
    public async Task InvokeAsync_ReturnsForbiddenForAuthenticatedUnauthorizedAccess()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(dbContext, isDevelopment: false, userId);
        var middleware = new ExceptionCatcherMiddleware(_ => throw new UnauthorizedAccessException("Only admins can access this resource."));

        await middleware.InvokeAsync(context);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);
        var errorLog = dbContext.ErrorLogs.Single();

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        response.Should().NotBeNull();
        response!.UserId.Should().Be(userId);
        errorLog.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        errorLog.UserId.Should().Be(userId);
    }

    [Test]
    public async Task InvokeAsync_MasksServerErrorMessageInProduction()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var context = CreateHttpContext(dbContext, isDevelopment: false);
        var middleware = new ExceptionCatcherMiddleware(_ => throw new ApplicationException("Database password leaked in stack context."));

        await middleware.InvokeAsync(context);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);
        var errorLog = dbContext.ErrorLogs.Single();

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        response.Should().NotBeNull();
        response!.Message.Should().Be("An unexpected error occurred.");
        response.StackTrace.Should().BeNull();
        errorLog.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Test]
    public async Task InvokeAsync_ReturnsServerErrorDetailsInDevelopment()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var context = CreateHttpContext(dbContext, isDevelopment: true);
        var middleware = new ExceptionCatcherMiddleware(_ => throw new ApplicationException("Development diagnostic detail."));

        await middleware.InvokeAsync(context);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        response.Should().NotBeNull();
        response!.Message.Should().Be("Development diagnostic detail.");
        response.StackTrace.Should().Contain("Development diagnostic detail.");
    }

    private static TestDataContext CreateContext(bool throwOnSave)
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options, throwOnSave);
    }

    private static DefaultHttpContext CreateHttpContext(
        BaseDataContext dbContext,
        bool isDevelopment,
        Guid? userId = null,
        string userIdClaimType = ClaimTypes.NameIdentifier)
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment(isDevelopment ? Environments.Development : Environments.Production))
            .AddSingleton<ILogger<ExceptionCatcherMiddleware>>(NullLogger<ExceptionCatcherMiddleware>.Instance)
            .AddSingleton(dbContext)
            .BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Response.Body = new MemoryStream();

        if (userId is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(userIdClaimType, userId.Value.ToString())],
                authenticationType: "Test"));
        }

        return context;
    }

    private static async Task<string> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private sealed class TestDataContext : BaseDataContext
    {
        private readonly bool _throwOnSave;

        public TestDataContext(DbContextOptions<BaseDataContext> options, bool throwOnSave) : base(options)
        {
            _throwOnSave = throwOnSave;
        }

        public int SaveChangesCalls { get; private set; }
        public int SaveChangesAsyncCalls { get; private set; }

        public override int SaveChanges()
        {
            SaveChangesCalls++;

            if (_throwOnSave)
                throw new InvalidOperationException("Simulated error-log persistence failure.");

            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesAsyncCalls++;

            if (_throwOnSave)
                throw new InvalidOperationException("Simulated error-log persistence failure.");

            return base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SavedFileDataDB>().Ignore(fileData => fileData.Metadata);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "MSAVA.API.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
