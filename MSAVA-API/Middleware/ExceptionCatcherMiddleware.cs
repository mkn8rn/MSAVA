using MSAVA_Shared.Models;
using MSAVA_INF.Models;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Claims;
using System.Text.Json;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using MSAVA_INF.Contexts;

namespace MSAVA_API.Middleware
{
    public class ExceptionCatcherMiddleware
    {
        private readonly RequestDelegate _next;

        public ExceptionCatcherMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var env = context.RequestServices.GetRequiredService<IHostEnvironment>();
            var logger = context.RequestServices.GetRequiredService<ILogger<ExceptionCatcherMiddleware>>();
            var dbContext = context.RequestServices.GetRequiredService<BaseDataContext>();
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                Guid errorId = Guid.NewGuid();
                DateTime timestamp = DateTime.UtcNow;
                int statusCode = GetStatusCode(ex);
                logger.LogError(ex, "Unhandled exception occurred: " + errorId);
                LogErrorToDb(errorId, timestamp, context, ex, dbContext, statusCode);
                await HandleExceptionAsync(errorId, timestamp, context, ex, env.IsDevelopment(), statusCode);
            }
        }

        private void LogErrorToDb(Guid errorId, DateTime timestamp, HttpContext context, Exception exception, BaseDataContext dbContext, int statusCode)
        {
            Guid? userId = null;
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                var userIdStr = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (Guid.TryParse(userIdStr, out var parsedId))
                {
                    userId = parsedId;
                }
            }

            var errorLog = new ErrorLogDB
            {
                Id = errorId,
                StatusCode = statusCode,
                Timestamp = timestamp,
                UserId = userId
            };
            dbContext.ErrorLogs.Add(errorLog);
            dbContext.SaveChanges();
        }

        private static int GetStatusCode(Exception exception)
        {
            int statusCode = StatusCodes.Status500InternalServerError;

            switch (exception)
            {
                // --- Use status code from exception ---
                case BadHttpRequestException badReq:
                    statusCode = badReq.StatusCode;
                    break;
                // --- 501 Not Implemented ---
                case PlatformNotSupportedException _:
                case NotImplementedException _:
                    statusCode = StatusCodes.Status501NotImplemented;
                    break;
                // --- 502 Bad Gateway ---
                case HttpRequestException _:
                case System.Net.Sockets.SocketException _:
                case System.Net.WebException _:
                    statusCode = StatusCodes.Status502BadGateway;
                    break;
                // --- 400 Bad Request ---
                case ArgumentNullException _:
                case ArgumentOutOfRangeException _:
                case ArgumentException _:
                case FormatException _:
                case InvalidDataException _:
                case SerializationException _:
                case System.Net.ProtocolViolationException _:
                case JsonException _:
                case System.Xml.XmlException _:
                case CryptographicException _:
                case IndexOutOfRangeException _:
                case OverflowException _:
                case DivideByZeroException _:
                case ObjectDisposedException _:
                case InvalidCastException:
                case NotFiniteNumberException:
                    statusCode = StatusCodes.Status400BadRequest;
                    break;
                // --- 401 Unauthorized ---
                case UnauthorizedAccessException _:
                case System.Security.Authentication.AuthenticationException _:
                case SecurityTokenException _:
                    statusCode = StatusCodes.Status401Unauthorized;
                    break;
                // --- 403 Forbidden ---
                case System.Security.SecurityException _:
                case AccessViolationException _:
                    statusCode = StatusCodes.Status403Forbidden;
                    break;
                // --- 404 Not Found ---
                case KeyNotFoundException _:
                case FileNotFoundException _:
                case DirectoryNotFoundException _:
                case FileLoadException _:
                    statusCode = StatusCodes.Status404NotFound;
                    break;
                // --- 405 Method Not Allowed ---
                case NotSupportedException _:
                    statusCode = StatusCodes.Status405MethodNotAllowed;
                    break;
                // --- 408 Request Timeout ---
                case TaskCanceledException _:
                    statusCode = StatusCodes.Status408RequestTimeout;
                    break;
                // --- 409 Conflict ---
                case DbUpdateConcurrencyException _:
                case InvalidOperationException _:
                case DbUpdateException _:
                    statusCode = StatusCodes.Status409Conflict;
                    break;
                // --- 414 URI Too Long ---
                case PathTooLongException _:
                    statusCode = StatusCodes.Status414UriTooLong;
                    break;
                // --- 422 Unprocessable Entity ---
                case ValidationException _:
                    statusCode = StatusCodes.Status422UnprocessableEntity;
                    break;
                // --- 500 Internal Server Error ---
                case AggregateException _:
                case DllNotFoundException _:
                case InsufficientMemoryException _:
                case ApplicationException _:
                case OutOfMemoryException _:
                case StackOverflowException _:
                case System.Data.DataException _:
                case ReflectionTypeLoadException _:
                case TypeLoadException _:
                case MissingMethodException _:
                case MemberAccessException _:
                case NullReferenceException _:
                case ExternalException _:
                    statusCode = StatusCodes.Status500InternalServerError;
                    break;
                // --- 503 Service Unavailable ---
                case OperationCanceledException _:
                case IOException _:
                    statusCode = StatusCodes.Status503ServiceUnavailable;
                    break;
                // --- 504 Gateway Timeout ---
                case TimeoutException _:
                    statusCode = StatusCodes.Status504GatewayTimeout;
                    break;
            }

            return statusCode;
        }

        private static async Task HandleExceptionAsync(Guid errorId, DateTime timestamp, HttpContext context, Exception exception, bool isDevelopment, int statusCode)
        {
            string message = exception.Message;
            string? stack = null;

            if (isDevelopment)
            {
                stack = exception.ToString();
            }

            Guid? userId = null;
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                var userIdStr = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (Guid.TryParse(userIdStr, out var parsedId))
                {
                    userId = parsedId;
                }
            }

            ErrorLogDTO responseDto = new ErrorLogDTO
            {
                Id = errorId,
                Message = message,
                StackTrace = stack,
                Timestamp = timestamp,
                UserId = userId
            };

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsync(JsonSerializer.Serialize(responseDto));
        }

    }
}
