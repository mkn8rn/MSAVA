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
        _logger = logger;
        _context = context;
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

        PersistLog(errorLog);
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

        PersistLog(inviteLog);
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

        PersistLog(groupLog);
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

        PersistLog(accessLog);
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

        PersistLog(accessLog);
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

        PersistLog(userLog);
    }

    private void PersistLog(object log)
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

            _context.SaveChanges();
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
