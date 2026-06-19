namespace MSAVA_Shared.Models;

public class FetchFileYouTubeDTO : IFileSupplementalMetadataRequest
{
    public required string YouTubeUrl { get; set; }
    public required Guid AccessGroupId { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? Categories { get; set; }
    public string? Description { get; set; } = string.Empty;
    public bool PublicViewing { get; set; } = false;
    public bool PublicDownload { get; set; } = false;
    public bool DownloadVideo { get; set; } = false;
    public bool DownloadAudio { get; set; } = false;
    public string? VideoQuality { get; set; }
    public string? AudioQuality { get; set; }
}
