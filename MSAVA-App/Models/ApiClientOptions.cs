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

        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException($"{UrlKey} must not contain user info, query, or fragment components.");
        }

        return EnsureTrailingSlash(uri);
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        if (uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
            return uri;

        return new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
    }
}
