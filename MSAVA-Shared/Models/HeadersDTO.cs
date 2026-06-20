namespace MSAVA_Shared.Models;

public class HeadersDTO
{
    public string? Authorization { get; set; }
    public string? ContentType { get; set; }
    public string? Accept { get; set; }
    public string? UserAgent { get; set; }
    public string? Host { get; set; }
    public string? Referer { get; set; }
    public string? Origin { get; set; }
    public string? AcceptEncoding { get; set; }
    public string? AcceptLanguage { get; set; }
    public string? CacheControl { get; set; }
    public string? Pragma { get; set; }
    public string? Cookie { get; set; }
    public string? XRequestedWith { get; set; }
    public string? XForwardedFor { get; set; }
    public string? Connection { get; set; }
    public Dictionary<string, string>? CustomHeaders { get; set; }
}
