namespace MSAVA_Shared.Models;

public class FetchFileGoogleDriveDTO : IFileSupplementalMetadataRequest
{
    public string FileUrl { get; set; } = string.Empty;
    public required Guid AccessGroupId { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? Categories { get; set; }
    public string? Description { get; set; } = string.Empty;
    public bool PublicViewing { get; set; } = false;
    public bool PublicDownload { get; set; } = false;
}
