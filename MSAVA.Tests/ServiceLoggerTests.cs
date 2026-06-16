using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;

namespace MSAVA_App.Tests;

public class ServiceLoggerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 16, 11, 0, 0, TimeSpan.Zero);

    [Test]
    public void Constructor_RejectsMissingLogger()
    {
        using var context = CreateContext();

        Action act = () => _ = new ServiceLogger(null!, context);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Test]
    public void Constructor_RejectsMissingContext()
    {
        Action act = () => _ = new ServiceLogger(NullLogger<ServiceLogger>.Instance, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("context");
    }

    [Test]
    public void LogInformation_SanitizesMessageBeforeWriting()
    {
        using var context = CreateContext();
        var captureLogger = new CapturingLogger();
        var logger = new ServiceLogger(captureLogger, context);

        logger.LogInformation("Download <file>\n& token\t\u0001");

        captureLogger.Messages.Should().ContainSingle()
            .Which.Should().Be("Download &lt;file&gt; &amp; token ");
    }

    [Test]
    public async Task WriteLogAsync_PersistsUserLogWithAsyncSave()
    {
        using var context = CreateContext();
        var logger = new ServiceLogger(
            NullLogger<ServiceLogger>.Instance,
            context,
            new FixedTimeProvider(FixedNow));
        var userId = Guid.NewGuid();

        await logger.WriteLogAsync(UserLogAction.AccountRegistered, "Registered <user>", userId, null);

        var log = context.UserLogs.Should().ContainSingle().Which;
        log.UserId.Should().Be(userId);
        log.AdminId.Should().BeNull();
        log.Action.Should().Be(UserLogAction.AccountRegistered);
        log.Timestamp.Should().Be(FixedNow.UtcDateTime);
        context.SaveChangesCalls.Should().Be(0);
        context.SaveChangesAsyncCalls.Should().Be(1);
    }

    [Test]
    public async Task WriteLogAsync_UsesInjectedClockForEveryPersistedLogType()
    {
        using var context = CreateContext();
        var logger = new ServiceLogger(
            NullLogger<ServiceLogger>.Instance,
            context,
            new FixedTimeProvider(FixedNow));
        var userId = Guid.NewGuid();
        var codeId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var firstRefId = Guid.NewGuid();
        var secondRefId = Guid.NewGuid();

        await logger.WriteLogAsync(500, "Failed", userId);
        await logger.WriteLogAsync(InviteLogActions.InviteCodeCreated, "Created invite", userId, codeId);
        await logger.WriteLogAsync(GroupLogActions.AccessGroupCreated, "Created group", userId, groupId);
        await logger.WriteLogAsync(AccessLogActions.NewFileCreated, "Created file", userId, "file.txt", firstRefId);
        await logger.WriteLogAsync(AccessLogActions.AccessViaFileStream, "Streamed file", userId, secondRefId);
        await logger.WriteLogAsync(UserLogAction.AccountRegistered, "Registered user", userId, adminId: null);

        context.ErrorLogs.Should().ContainSingle()
            .Which.Timestamp.Should().Be(FixedNow.UtcDateTime);
        context.InviteLogs.Should().ContainSingle()
            .Which.Timestamp.Should().Be(FixedNow.UtcDateTime);
        context.GroupLogs.Should().ContainSingle()
            .Which.Timestamp.Should().Be(FixedNow.UtcDateTime);
        context.AccessLogs.Should().HaveCount(2)
            .And.AllSatisfy(log => log.Timestamp.Should().Be(FixedNow.UtcDateTime));
        context.UserLogs.Should().ContainSingle()
            .Which.Timestamp.Should().Be(FixedNow.UtcDateTime);
        context.SaveChangesAsyncCalls.Should().Be(6);
    }

    [Test]
    public async Task WriteLogAsync_PropagatesCallerCancellationAndDetachesPendingLog()
    {
        using var context = CreateContext();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var userId = Guid.NewGuid();

        var act = async () => await logger.WriteLogAsync(
            UserLogAction.AccountRegistered,
            "Registered user",
            userId,
            adminId: null,
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        context.ChangeTracker.Entries<UserLogDB>().Should().BeEmpty();
        context.UserLogs.Should().BeEmpty();
        context.SaveChangesCalls.Should().Be(0);
        context.SaveChangesAsyncCalls.Should().Be(1);
    }

    [Test]
    public async Task WriteLogAsync_DetachesPendingLogAndContinuesWhenPersistenceFailureIsRecoverable()
    {
        using var context = CreateThrowingContext(new InvalidOperationException("Simulated log persistence failure."));
        var captureLogger = new CapturingLogger();
        var logger = new ServiceLogger(captureLogger, context);
        var userId = Guid.NewGuid();

        await logger.WriteLogAsync(
            UserLogAction.AccountRegistered,
            "Registered user",
            userId,
            adminId: null);

        context.ChangeTracker.Entries<UserLogDB>().Should().BeEmpty();
        context.UserLogs.Should().BeEmpty();
        context.SaveChangesAsyncCalls.Should().Be(1);
        captureLogger.Messages.Should().Contain(message =>
            message.Contains("Failed to persist UserLogDB to database", StringComparison.Ordinal));
    }

    [Test]
    public async Task WriteLogAsync_DetachesPendingLogAndPropagatesCriticalPersistenceFailure()
    {
        using var context = CreateThrowingContext(new OutOfMemoryException("Critical log persistence failure."));
        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var userId = Guid.NewGuid();

        var act = async () => await logger.WriteLogAsync(
            UserLogAction.AccountRegistered,
            "Registered user",
            userId,
            adminId: null);

        await act.Should().ThrowAsync<OutOfMemoryException>()
            .WithMessage("Critical log persistence failure.");
        context.ChangeTracker.Entries<UserLogDB>().Should().BeEmpty();
        context.UserLogs.Should().BeEmpty();
        context.SaveChangesAsyncCalls.Should().Be(1);
    }

    [Test]
    public async Task WriteLogAsync_DetachesPendingLogAndPropagatesWrappedCancellation()
    {
        using var context = CreateThrowingContext(new InvalidOperationException(
            "Save failed after cancellation.",
            new OperationCanceledException("Request was canceled.")));
        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var userId = Guid.NewGuid();

        var act = async () => await logger.WriteLogAsync(
            UserLogAction.AccountRegistered,
            "Registered user",
            userId,
            adminId: null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Save failed after cancellation.");
        context.ChangeTracker.Entries<UserLogDB>().Should().BeEmpty();
        context.UserLogs.Should().BeEmpty();
        context.SaveChangesAsyncCalls.Should().Be(1);
    }

    private static TestDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private static ThrowingDataContext CreateThrowingContext(Exception saveException)
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ThrowingDataContext(options, saveException);
    }

    private sealed class TestDataContext : BaseDataContext
    {
        public TestDataContext(DbContextOptions<BaseDataContext> options) : base(options)
        {
        }

        public int SaveChangesCalls { get; private set; }
        public int SaveChangesAsyncCalls { get; private set; }

        public override int SaveChanges()
        {
            SaveChangesCalls++;
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesAsyncCalls++;
            return base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SavedFileDataDB>().Ignore(fileData => fileData.Metadata);
        }
    }

    private sealed class ThrowingDataContext : BaseDataContext
    {
        private readonly Exception _saveException;

        public ThrowingDataContext(
            DbContextOptions<BaseDataContext> options,
            Exception saveException)
            : base(options)
        {
            _saveException = saveException;
        }

        public int SaveChangesAsyncCalls { get; private set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesAsyncCalls++;
            return Task.FromException<int>(_saveException);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SavedFileDataDB>().Ignore(fileData => fileData.Metadata);
        }
    }

    private sealed class CapturingLogger : ILogger<ServiceLogger>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
