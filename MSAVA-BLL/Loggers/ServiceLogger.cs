using MSAVA_INF.Models;
using MSAVA_INF.Contexts;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;

namespace MSAVA_BLL.Loggers;

public class ServiceLogger : IDisposable
{
    private readonly ILogger<ServiceLogger> _logger;
    private readonly BaseDataContext _context;
    private readonly ConcurrentQueue<object> _logQueue = new();
    private readonly Timer _flushTimer;
    private readonly object _flushLock = new();
    private bool _disposed;

    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);
    private const int MaxBatchSize = 100;

    public ServiceLogger(ILogger<ServiceLogger> logger, BaseDataContext context)
    {
        _logger = logger;
        _context = context;
        _flushTimer = new Timer(_ => FlushLogs(), null, FlushInterval, FlushInterval);
    }

    public void LogInformation(string message)
    {
        _logger.LogInformation("{Message}", message);
    }

    public string SanitizeString(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        var sb = new StringBuilder(Math.Min(message.Length * 2, 200));
        int maxLen = Math.Min(message.Length, 200);

        for (int i = 0; i < maxLen; i++)
        {
            char c = message[i];
            string? replacement = c switch
            {
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                '&' => "&amp;",
                '\n' or '\r' => " ",
                '\t' => " ",
                _ when char.IsControl(c) => "",
                _ => null
            };

            if (replacement != null)
                sb.Append(replacement);
            else
                sb.Append(c);
        }

        return sb.ToString();
    }

    public void WriteLog(int statusCode, string message, Guid? userId)
    {
        var errorLog = new ErrorLogDB
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StatusCode = statusCode,
            Timestamp = DateTime.UtcNow
        };

        string sanitizedMessage = SanitizeString(message);
        _logger.LogError("Status Code: {StatusCode}, Message: {Message}, UserId: {UserId}", statusCode, sanitizedMessage, userId);

        _logQueue.Enqueue(errorLog);
        TryFlushIfFull();
    }

    public void WriteLog(InviteLogActions action, string message, Guid userId, Guid codeId)
    {
        var inviteLog = new InviteLogDB
        {
            Id = Guid.NewGuid(),
            Action = action,
            UserId = userId,
            InviteCodeId = codeId,
            Timestamp = DateTime.UtcNow
        };

        string sanitizedMessage = SanitizeString(message);
        _logger.LogInformation("Action: {Action}, Message: {Message}, UserId: {UserId}, InviteCodeId: {CodeId}", action, sanitizedMessage, userId, codeId);

        _logQueue.Enqueue(inviteLog);
        TryFlushIfFull();
    }

    public void WriteLog(GroupLogActions action, string message, Guid userId, Guid groupId)
    {
        var groupLog = new GroupLogDB
        {
            Id = Guid.NewGuid(),
            Action = action,
            UserId = userId,
            GroupId = groupId,
            Timestamp = DateTime.UtcNow
        };

        string sanitizedMessage = SanitizeString(message);
        _logger.LogInformation("Action: {Action}, Message: {Message}, UserId: {UserId}, GroupId: {GroupId}", action, sanitizedMessage, userId, groupId);

        _logQueue.Enqueue(groupLog);
        TryFlushIfFull();
    }

    public void WriteLog(AccessLogActions action, string message, Guid userId, string fileNameWithExtension, Guid refId)
    {
        var accessLog = new AccessLogDB
        {
            Id = Guid.NewGuid(),
            Action = action,
            UserId = userId,
            FileRefId = refId,
            Timestamp = DateTime.UtcNow
        };

        string sanitizedMessage = SanitizeString(message);
        string sanitizedFileName = SanitizeString(fileNameWithExtension);
        _logger.LogInformation("Action: {Action}, Message: {Message}, UserId: {UserId}, File: {FileName}, FileRefId: {RefId}", action, sanitizedMessage, userId, sanitizedFileName, refId);

        _logQueue.Enqueue(accessLog);
        TryFlushIfFull();
    }

    public void WriteLog(AccessLogActions action, string message, Guid userId, Guid refId)
    {
        var accessLog = new AccessLogDB
        {
            Id = Guid.NewGuid(),
            Action = action,
            UserId = userId,
            FileRefId = refId,
            Timestamp = DateTime.UtcNow
        };

        string sanitizedMessage = SanitizeString(message);
        _logger.LogInformation("Action: {Action}, Message: {Message}, UserId: {UserId}, FileRefId: {RefId}", action, sanitizedMessage, userId, refId);

        _logQueue.Enqueue(accessLog);
        TryFlushIfFull();
    }

    public void WriteLog(UserLogAction action, string message, Guid userId, Guid? adminId)
    {
        var userLog = new UserLogDB
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AdminId = adminId,
            Action = action,
            Timestamp = DateTime.UtcNow
        };

        string sanitizedMessage = SanitizeString(message);
        _logger.LogInformation("Action: {Action}, Message: {Message}, UserId: {UserId}, AdminId: {AdminId}", action, sanitizedMessage, userId, adminId);

        _logQueue.Enqueue(userLog);
        TryFlushIfFull();
    }

    private void TryFlushIfFull()
    {
        if (_logQueue.Count >= MaxBatchSize)
            FlushLogs();
    }

    private void FlushLogs()
    {
        if (_logQueue.IsEmpty) return;

        lock (_flushLock)
        {
            if (_logQueue.IsEmpty) return;

            var batch = new List<object>(MaxBatchSize);
            while (batch.Count < MaxBatchSize && _logQueue.TryDequeue(out var log))
            {
                batch.Add(log);
            }

            if (batch.Count == 0) return;

            try
            {
                foreach (var log in batch)
                {
                    switch (log)
                    {
                        case ErrorLogDB e: _context.ErrorLogs.Add(e); break;
                        case InviteLogDB i: _context.InviteLogs.Add(i); break;
                        case GroupLogDB g: _context.GroupLogs.Add(g); break;
                        case AccessLogDB a: _context.AccessLogs.Add(a); break;
                        case UserLogDB u: _context.UserLogs.Add(u); break;
                    }
                }
                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush {Count} log entries to database", batch.Count);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _flushTimer.Dispose();
        FlushLogs(); // Final flush
    }
}
