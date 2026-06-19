using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
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
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Constructor_RejectsMissingNextDelegate()
    {
        Action act = () => _ = new ExceptionCatcherMiddleware(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("next");
    }

    [Test]
    public async Task InvokeAsync_ReturnsSanitizedErrorResponseWhenDatabaseLoggingFails()
    {
        using var dbContext = CreateContext(throwOnSave: true);
        var context = CreateHttpContext();
        var middleware = new ExceptionCatcherMiddleware(_ => throw new KeyNotFoundException("File reference e7efb446-cc57-4bee-a477-cd89a3670db9 missing."));

        await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        context.Response.ContentType.Should().Be("application/json");
        response.Should().NotBeNull();
        response!.Message.Should().Be("The requested resource was not found.");
        response.StackTrace.Should().BeNull();
        dbContext.ChangeTracker.Entries<ErrorLogDB>().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_DetachesAndPropagatesCriticalErrorLogPersistenceFailure()
    {
        using var dbContext = CreateContext(new InvalidOperationException(
            "Wrapped cancellation while saving error log.",
            new OperationCanceledException("Request was canceled.")));
        var context = CreateHttpContext();
        var middleware = new ExceptionCatcherMiddleware(_ => throw new KeyNotFoundException("File reference missing."));

        var act = async () => await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Wrapped cancellation while saving error log.");
        dbContext.ChangeTracker.Entries<ErrorLogDB>().Should().BeEmpty();
        dbContext.ErrorLogs.Should().BeEmpty();
        dbContext.SaveChangesCalls.Should().Be(0);
        dbContext.SaveChangesAsyncCalls.Should().Be(1);
        context.Response.ContentType.Should().BeNull();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        (await ReadResponseBodyAsync(context)).Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_PersistsErrorLogWithAuthenticatedUserWhenDatabaseLoggingSucceeds()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(userId);
        var middleware = new ExceptionCatcherMiddleware(
            _ => throw new ArgumentException("Bad query."),
            new FixedTimeProvider(FixedNow));

        await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);
        var errorLog = dbContext.ErrorLogs.Single();

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        response.Should().NotBeNull();
        response!.Message.Should().Be("Bad query.");
        response!.UserId.Should().Be(userId);
        response.Timestamp.Should().Be(FixedNow.UtcDateTime);
        errorLog.Id.Should().Be(response.Id);
        errorLog.UserId.Should().Be(userId);
        errorLog.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        errorLog.Timestamp.Should().Be(FixedNow.UtcDateTime);
        dbContext.SaveChangesCalls.Should().Be(0);
        dbContext.SaveChangesAsyncCalls.Should().Be(1);
    }

    [Test]
    public async Task InvokeAsync_LogsErrorIdAsStructuredProperty()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var context = CreateHttpContext();
        var logger = new CapturingLogger<ExceptionCatcherMiddleware>();
        var middleware = new ExceptionCatcherMiddleware(
            _ => throw new ArgumentException("Bad query."),
            new FixedTimeProvider(FixedNow));

        await InvokeMiddlewareAsync(
            middleware,
            context,
            dbContext,
            isDevelopment: false,
            logger);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);

        response.Should().NotBeNull();
        var message = logger.Messages.Should().ContainSingle(log =>
                log.Level == LogLevel.Error &&
                log.Message.StartsWith("Unhandled exception occurred:", StringComparison.Ordinal))
            .Subject;
        message.Properties.Should().ContainKey("ErrorId")
            .WhoseValue.Should().Be(response!.Id);
        message.Message.Should().Contain(response.Id.ToString());
    }

    [Test]
    public async Task InvokeAsync_PassesRequestCancellationTokenToErrorLogPersistence()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        using var cancellationTokenSource = new CancellationTokenSource();
        var context = CreateHttpContext();
        context.RequestAborted = cancellationTokenSource.Token;
        var middleware = new ExceptionCatcherMiddleware(
            _ => throw new ArgumentException("Bad query."),
            new FixedTimeProvider(FixedNow));

        await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        dbContext.SaveChangesAsyncTokens.Should().ContainSingle()
            .Which.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task InvokeAsync_PersistsErrorLogWithJwtSubjectWhenNameIdentifierIsMissing()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(userId, JwtRegisteredClaimNames.Sub);
        var middleware = new ExceptionCatcherMiddleware(_ => throw new ArgumentException("Bad query."));

        await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

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
        var context = CreateHttpContext();
        var middleware = new ExceptionCatcherMiddleware(_ => throw new UnauthorizedAccessException("Session user is required."));

        await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);
        var errorLog = dbContext.ErrorLogs.Single();

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        response.Should().NotBeNull();
        response!.Message.Should().Be("Authentication is required.");
        response.UserId.Should().BeNull();
        errorLog.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        errorLog.UserId.Should().BeNull();
    }

    [Test]
    public async Task InvokeAsync_ReturnsForbiddenForAuthenticatedUnauthorizedAccess()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(userId);
        var middleware = new ExceptionCatcherMiddleware(_ => throw new UnauthorizedAccessException("Only admins can access this resource."));

        await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);
        var errorLog = dbContext.ErrorLogs.Single();

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        response.Should().NotBeNull();
        response!.Message.Should().Be("Access to the requested resource is forbidden.");
        response.UserId.Should().Be(userId);
        errorLog.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        errorLog.UserId.Should().Be(userId);
    }

    [Test]
    public async Task InvokeAsync_MasksProductionConflictDetails()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var context = CreateHttpContext();
        var middleware = new ExceptionCatcherMiddleware(_ => throw new InvalidOperationException("Duplicate user records were found for username admin@example.test."));

        await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);
        var errorLog = dbContext.ErrorLogs.Single();

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        response.Should().NotBeNull();
        response!.Message.Should().Be("The request conflicts with the current resource state.");
        response.Message.Should().NotContain("admin@example.test");
        errorLog.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Test]
    public async Task InvokeAsync_MasksProductionUpstreamFailureDetails()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var context = CreateHttpContext();
        var middleware = new ExceptionCatcherMiddleware(_ => throw new HttpRequestException(
            "Google Drive download failed 502: provider-token=secret-value",
            null,
            HttpStatusCode.BadGateway));

        await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);
        var errorLog = dbContext.ErrorLogs.Single();

        context.Response.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
        response.Should().NotBeNull();
        response!.Message.Should().Be("A dependent service request failed.");
        response.Message.Should().NotContain("secret-value");
        errorLog.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Test]
    public async Task InvokeAsync_MasksBadHttpRequestDetailsInProduction()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var context = CreateHttpContext();
        var middleware = new ExceptionCatcherMiddleware(
            _ => throw new BadHttpRequestException(
                "Malformed request body near C:\\tenant-secrets\\upload.tmp.",
                StatusCodes.Status400BadRequest));

        await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);
        var errorLog = dbContext.ErrorLogs.Single();

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        response.Should().NotBeNull();
        response!.Message.Should().Be("The request is invalid.");
        response.Message.Should().NotContain("tenant-secrets");
        response.Message.Should().NotContain("upload.tmp");
        errorLog.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task InvokeAsync_ReturnsPayloadTooLargeForFileTooLargeException()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(userId);
        var middleware = new ExceptionCatcherMiddleware(_ => throw new FileTooLargeException(5, 4));

        await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);
        var errorLog = dbContext.ErrorLogs.Single();

        context.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        response.Should().NotBeNull();
        response!.Message.Should().Be("File size 5 bytes exceeds the maximum allowed size of 4 bytes.");
        response.UserId.Should().Be(userId);
        errorLog.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        errorLog.UserId.Should().Be(userId);
    }

    [Test]
    public async Task InvokeAsync_MasksServerErrorMessageInProduction()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var context = CreateHttpContext();
        var middleware = new ExceptionCatcherMiddleware(_ => throw new ApplicationException("Database password leaked in stack context."));

        await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

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
    public async Task InvokeAsync_PropagatesCriticalExceptionsWithoutLoggingOrWritingResponse()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var context = CreateHttpContext();
        var middleware = new ExceptionCatcherMiddleware(_ => throw new AccessViolationException("Native memory boundary failed."));

        var act = async () => await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        await act.Should().ThrowAsync<AccessViolationException>()
            .WithMessage("Native memory boundary failed.");
        dbContext.ErrorLogs.Should().BeEmpty();
        dbContext.SaveChangesCalls.Should().Be(0);
        dbContext.SaveChangesAsyncCalls.Should().Be(0);
        context.Response.ContentType.Should().BeNull();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        (await ReadResponseBodyAsync(context)).Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_PropagatesAggregateCriticalExceptionsWithoutLoggingOrWritingResponse()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var context = CreateHttpContext();
        var criticalException = new AccessViolationException("Native memory boundary failed.");
        var aggregateException = new AggregateException(
            "Multiple pipeline operations failed.",
            new InvalidOperationException("Recoverable operation failed."),
            criticalException);
        var middleware = new ExceptionCatcherMiddleware(_ => throw aggregateException);

        var act = async () => await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown.Which.InnerExceptions.Should().Contain(criticalException);
        dbContext.ErrorLogs.Should().BeEmpty();
        dbContext.SaveChangesCalls.Should().Be(0);
        dbContext.SaveChangesAsyncCalls.Should().Be(0);
        context.Response.ContentType.Should().BeNull();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        (await ReadResponseBodyAsync(context)).Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_TreatsRuntimeProgrammingFaultAsMaskedServerError()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var context = CreateHttpContext();
        var middleware = new ExceptionCatcherMiddleware(_ => throw new InvalidCastException("Internal cast failed."));

        await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

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
        var context = CreateHttpContext();
        var middleware = new ExceptionCatcherMiddleware(_ => throw new ApplicationException("Development diagnostic detail."));

        await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: true);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        response.Should().NotBeNull();
        response!.Message.Should().Be("Development diagnostic detail.");
        response.StackTrace.Should().Contain("Development diagnostic detail.");
    }

    [Test]
    public async Task InvokeAsync_PropagatesRequestAbortedCancellationWithoutLoggingError()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var context = CreateHttpContext();
        context.RequestAborted = cancellation.Token;
        var middleware = new ExceptionCatcherMiddleware(_ => throw new OperationCanceledException(cancellation.Token));

        var act = async () => await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        await act.Should().ThrowAsync<OperationCanceledException>();
        dbContext.ErrorLogs.Should().BeEmpty();
        dbContext.SaveChangesCalls.Should().Be(0);
        dbContext.SaveChangesAsyncCalls.Should().Be(0);
        context.Response.ContentType.Should().BeNull();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        (await ReadResponseBodyAsync(context)).Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ReturnsTimeoutForNonAbortedOperationCancellation()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var context = CreateHttpContext();
        var middleware = new ExceptionCatcherMiddleware(
            _ => throw new OperationCanceledException("Dependent operation timed out."),
            new FixedTimeProvider(FixedNow));

        await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        var body = await ReadResponseBodyAsync(context);
        var response = JsonSerializer.Deserialize<ErrorLogDTO>(body);
        var errorLog = dbContext.ErrorLogs.Single();

        context.Response.StatusCode.Should().Be(StatusCodes.Status408RequestTimeout);
        response.Should().NotBeNull();
        response!.Message.Should().Be("The request timed out.");
        response.StackTrace.Should().BeNull();
        response.Timestamp.Should().Be(FixedNow.UtcDateTime);
        errorLog.StatusCode.Should().Be(StatusCodes.Status408RequestTimeout);
        errorLog.Timestamp.Should().Be(FixedNow.UtcDateTime);
    }

    [Test]
    public async Task InvokeAsync_PropagatesExceptionWhenResponseAlreadyStarted()
    {
        using var dbContext = CreateContext(throwOnSave: false);
        var context = CreateHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature(context.Response.Body));
        var middleware = new ExceptionCatcherMiddleware(_ => throw new InvalidOperationException("Response is already partially written."));

        var act = async () => await InvokeMiddlewareAsync(middleware, context, dbContext, isDevelopment: false);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Response is already partially written.");
        dbContext.ErrorLogs.Should().BeEmpty();
        dbContext.SaveChangesCalls.Should().Be(0);
        dbContext.SaveChangesAsyncCalls.Should().Be(0);
        context.Response.ContentType.Should().BeNull();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        (await ReadResponseBodyAsync(context)).Should().BeEmpty();
    }

    private static TestDataContext CreateContext(bool throwOnSave)
    {
        return CreateContext(throwOnSave
            ? new InvalidOperationException("Simulated error-log persistence failure.")
            : null);
    }

    private static TestDataContext CreateContext(Exception? saveException)
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options, saveException);
    }

    private static Task InvokeMiddlewareAsync(
        ExceptionCatcherMiddleware middleware,
        HttpContext context,
        BaseDataContext dbContext,
        bool isDevelopment,
        ILogger<ExceptionCatcherMiddleware>? logger = null)
    {
        return middleware.InvokeAsync(
            context,
            new TestHostEnvironment(isDevelopment ? Environments.Development : Environments.Production),
            logger ?? NullLogger<ExceptionCatcherMiddleware>.Instance,
            dbContext);
    }

    private static DefaultHttpContext CreateHttpContext(
        Guid? userId = null,
        string userIdClaimType = ClaimTypes.NameIdentifier)
    {
        var context = new DefaultHttpContext();
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
        private readonly Exception? _saveException;

        public TestDataContext(DbContextOptions<BaseDataContext> options, Exception? saveException) : base(options)
        {
            _saveException = saveException;
        }

        public int SaveChangesCalls { get; private set; }
        public int SaveChangesAsyncCalls { get; private set; }
        public List<CancellationToken> SaveChangesAsyncTokens { get; } = [];

        public override int SaveChanges()
        {
            SaveChangesCalls++;

            if (_saveException is not null)
                throw _saveException;

            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesAsyncCalls++;
            SaveChangesAsyncTokens.Add(cancellationToken);

            if (_saveException is not null)
                throw _saveException;

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

    private sealed class StartedResponseFeature(Stream body) : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = body;

        public bool HasStarted => true;

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<CapturedLogMessage> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(new CapturedLogMessage(
                logLevel,
                eventId,
                exception,
                formatter(state, exception),
                CaptureProperties(state)));
        }

        private static IReadOnlyDictionary<string, object?> CaptureProperties<TState>(TState state)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> pairs)
                return new Dictionary<string, object?>();

            return pairs.ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }

    private sealed record CapturedLogMessage(
        LogLevel Level,
        EventId EventId,
        Exception? Exception,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
