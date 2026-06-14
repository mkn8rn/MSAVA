using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;

namespace MSAVA_App.Tests;

public class ServiceLoggerTests
{
    [Test]
    public async Task WriteLogAsync_PersistsUserLogWithAsyncSave()
    {
        using var context = CreateContext();
        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var userId = Guid.NewGuid();

        await logger.WriteLogAsync(UserLogAction.AccountRegistered, "Registered <user>", userId, null);

        context.UserLogs.Should().ContainSingle(log =>
            log.UserId == userId &&
            log.AdminId == null &&
            log.Action == UserLogAction.AccountRegistered);
        context.SaveChangesCalls.Should().Be(0);
        context.SaveChangesAsyncCalls.Should().Be(1);
    }

    private static TestDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
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
}
