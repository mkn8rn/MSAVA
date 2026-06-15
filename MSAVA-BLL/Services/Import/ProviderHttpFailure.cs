using MSAVA_BLL.Utils;

namespace MSAVA_BLL.Services.Import;

internal static class ProviderHttpFailure
{
    private const int MaximumErrorBodyLength = 2048;

    public static async Task ThrowAsync(
        string operation,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body = await HttpErrorBodyReader.ReadTrimmedBodyAsync(
            response.Content,
            MaximumErrorBodyLength,
            cancellationToken);
        string message = string.IsNullOrWhiteSpace(body)
            ? $"{operation} failed {(int)response.StatusCode} ({response.ReasonPhrase ?? response.StatusCode.ToString()})"
            : $"{operation} failed {(int)response.StatusCode}: {body}";

        throw new HttpRequestException(message, null, response.StatusCode);
    }
}
