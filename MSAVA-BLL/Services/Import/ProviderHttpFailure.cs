namespace MSAVA_BLL.Services.Import;

internal static class ProviderHttpFailure
{
    private const int MaximumErrorBodyLength = 2048;

    public static async Task ThrowAsync(
        string operation,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body = await ReadErrorBodyAsync(response, cancellationToken);
        string message = string.IsNullOrWhiteSpace(body)
            ? $"{operation} failed {(int)response.StatusCode} ({response.ReasonPhrase ?? response.StatusCode.ToString()})"
            : $"{operation} failed {(int)response.StatusCode}: {body}";

        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static async Task<string> ReadErrorBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content is null)
            return string.Empty;

        string body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();

        if (body.Length <= MaximumErrorBodyLength)
            return body;

        return body[..MaximumErrorBodyLength];
    }
}
