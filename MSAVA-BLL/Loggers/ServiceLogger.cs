using MSAVA_INF.Models;
using MSAVA_INF.Contexts;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace MSAVA_BLL.Loggers;

public class ServiceLogger
{
    private readonly ILogger<ServiceLogger> _logger;
    private readonly BaseDataContext _context;

    public ServiceLogger(ILogger<ServiceLogger> logger, BaseDataContext context)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _context = context ?? throw new ArgumentNullException(nameof(context));
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

    public async Task WriteLogAsync(
        int statusCode,
        string message,
        Guid? userId,
        CancellationToken cancellationToken = default)
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

        await PersistLogAsync(errorLog, cancellationToken);
    }

    public async Task WriteLogAsync(
        InviteLogActions action,
        string message,
        Guid userId,
        Guid codeId,
        CancellationToken cancellationToken = default)
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

        await PersistLogAsync(inviteLog, cancellationToken);
    }

    public async Task WriteLogAsync(
        GroupLogActions action,
        string message,
        Guid userId,
        Guid groupId,
        CancellationToken cancellationToken = default)
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

        await PersistLogAsync(groupLog, cancellationToken);
    }

    public async Task WriteLogAsync(
        AccessLogActions action,
        string message,
        Guid userId,
        string fileNameWithExtension,
        Guid refId,
        CancellationToken cancellationToken = default)
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

        await PersistLogAsync(accessLog, cancellationToken);
    }

    public async Task WriteLogAsync(
        AccessLogActions action,
        string message,
        Guid userId,
        Guid refId,
        CancellationToken cancellationToken = default)
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

        await PersistLogAsync(accessLog, cancellationToken);
    }

    public async Task WriteLogAsync(
        UserLogAction action,
        string message,
        Guid userId,
        Guid? adminId,
        CancellationToken cancellationToken = default)
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

        await PersistLogAsync(userLog, cancellationToken);
    }

    private async Task PersistLogAsync(object log, CancellationToken cancellationToken)
    {
        try
        {
            switch (log)
            {
                case ErrorLogDB e: _context.ErrorLogs.Add(e); break;
                case InviteLogDB i: _context.InviteLogs.Add(i); break;
                case GroupLogDB g: _context.GroupLogs.Add(g); break;
                case AccessLogDB a: _context.AccessLogs.Add(a); break;
                case UserLogDB u: _context.UserLogs.Add(u); break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(log), log.GetType().FullName, "Unsupported service log type.");
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DetachLog(log);
            throw;
        }
        catch (Exception ex)
        {
            DetachLog(log);
            _logger.LogError(ex, "Failed to persist {LogType} to database", log.GetType().Name);
        }
    }

    private void DetachLog(object log)
    {
        var entry = _context.Entry(log);
        if (entry.State != EntityState.Detached)
            entry.State = EntityState.Detached;
    }
}
