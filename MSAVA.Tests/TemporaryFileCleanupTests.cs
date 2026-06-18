using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_INF.Utils;

namespace MSAVA_App.Tests;

public class TemporaryFileCleanupTests
{
    [Test]
    public void DeleteIfPresent_DeletesExistingFile()
    {
        string tempFilePath = Path.GetTempFileName();

        try
        {
            TemporaryFileCleanup.DeleteIfPresent(tempFilePath, NullLogger.Instance);

            File.Exists(tempFilePath).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
        }
    }

    [Test]
    public void DeleteIfPresent_LogsWarningWhenDeleteFails()
    {
        var logger = new CapturingLogger();
        var deleteException = new IOException("Simulated cleanup failure.");

        TemporaryFileCleanup.DeleteIfPresent(
            "C:\\temp\\stuck.tmp",
            logger,
            _ => true,
            _ => throw deleteException);

        logger.Messages.Should().ContainSingle(message =>
            message.Level == LogLevel.Warning &&
            message.Exception == deleteException &&
            message.Text.Contains("Failed to delete temporary file", StringComparison.Ordinal) &&
            message.Text.Contains("C:\\temp\\stuck.tmp", StringComparison.Ordinal));
    }

    [Test]
    public void DeleteIfPresent_LogsWarningWhenExistenceCheckFails()
    {
        var logger = new CapturingLogger();
        var existsException = new UnauthorizedAccessException("Simulated existence check failure.");
        bool deleteCalled = false;

        TemporaryFileCleanup.DeleteIfPresent(
            "C:\\temp\\hidden.tmp",
            logger,
            _ => throw existsException,
            _ => deleteCalled = true);

        deleteCalled.Should().BeFalse();
        logger.Messages.Should().ContainSingle(message =>
            message.Level == LogLevel.Warning &&
            message.Exception == existsException &&
            message.Text.Contains("Failed to delete temporary file", StringComparison.Ordinal) &&
            message.Text.Contains("C:\\temp\\hidden.tmp", StringComparison.Ordinal));
    }

    [Test]
    public void DeleteIfPresent_PropagatesCriticalDeleteFailure()
    {
        var deleteException = new OutOfMemoryException("Critical cleanup failure.");

        Action act = () => TemporaryFileCleanup.DeleteIfPresent(
            "C:\\temp\\stuck.tmp",
            NullLogger.Instance,
            _ => true,
            _ => throw deleteException);

        act.Should().Throw<OutOfMemoryException>()
            .WithMessage("Critical cleanup failure.");
    }

    [Test]
    public void DeleteIfPresent_PropagatesCriticalExistenceCheckFailure()
    {
        var existsException = new OutOfMemoryException("Critical cleanup inspection failure.");

        Action act = () => TemporaryFileCleanup.DeleteIfPresent(
            "C:\\temp\\hidden.tmp",
            NullLogger.Instance,
            _ => throw existsException,
            _ => throw new InvalidOperationException("Delete should not be reached."));

        act.Should().Throw<OutOfMemoryException>()
            .WithMessage("Critical cleanup inspection failure.");
    }

    [Test]
    public void DeleteIfPresent_IgnoresBlankPath()
    {
        bool deleteCalled = false;

        TemporaryFileCleanup.DeleteIfPresent(
            " ",
            NullLogger.Instance,
            _ => true,
            _ => deleteCalled = true);

        deleteCalled.Should().BeFalse();
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<LogMessage> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(new LogMessage(logLevel, exception, formatter(state, exception)));
        }
    }

    private sealed record LogMessage(LogLevel Level, Exception? Exception, string Text);
}
