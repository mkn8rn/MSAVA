namespace MSAVA_App.Models;

public sealed class ApiClientOptions
{
    public const string SectionName = "ApiClient";
    public const string UrlKey = SectionName + ":Url";

    public string? Url { get; init; }

    public Uri RequireBaseAddress()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"{UrlKey} must be an absolute HTTP or HTTPS URL.");
        }

        return uri;
    }
}
