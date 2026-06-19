using MSAVA_BLL.Utils;

namespace MSAVA_BLL.Services.Import;

internal static class ProviderHttpFailure
{
    public static Task ThrowAsync(
        string operation,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        string message = $"{operation} failed {HttpFailureMessage.FormatStatus(response)}";

        return Task.FromException(new HttpRequestException(message, null, response.StatusCode));
    }
}
