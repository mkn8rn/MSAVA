using MSAVA_INF.Contexts;

namespace MSAVA_API.Authorization;

public sealed class PublicFileAccessMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _publicFilesDirectory;
    private readonly PathString _requestPath;

    public PublicFileAccessMiddleware(
        RequestDelegate next,
        string publicFilesDirectory,
        string requestPath)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        ArgumentException.ThrowIfNullOrWhiteSpace(publicFilesDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestPath);

        _publicFilesDirectory = Path.GetFullPath(publicFilesDirectory);
        _requestPath = new PathString(requestPath);
    }

    public async Task InvokeAsync(
        HttpContext context,
        BaseDataContext dbContext,
        ILogger<PublicFileAccessMiddleware> logger)
    {
        if (!TryResolvePublicFileRequest(context, out string? physicalPath) ||
            !File.Exists(physicalPath))
        {
            await _next(context);
            return;
        }

        if (!PublicFileAccessGuard.CanServePublicFile(dbContext, logger, physicalPath))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }

    private bool TryResolvePublicFileRequest(HttpContext context, out string? physicalPath)
    {
        physicalPath = null;

        if (!HttpMethods.IsGet(context.Request.Method) &&
            !HttpMethods.IsHead(context.Request.Method))
        {
            return false;
        }

        if (!context.Request.Path.StartsWithSegments(_requestPath, out PathString remainingPath))
            return false;

        string? relativePath = remainingPath.Value?.TrimStart('/');
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        string pathUnderPublicDirectory = relativePath.Replace('/', Path.DirectorySeparatorChar);
        physicalPath = Path.GetFullPath(Path.Combine(_publicFilesDirectory, pathUnderPublicDirectory));
        return true;
    }
}
