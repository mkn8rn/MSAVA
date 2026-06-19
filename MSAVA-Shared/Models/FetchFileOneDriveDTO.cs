namespace MSAVA_Shared.Models;

public class FetchFileFromOneDriveDTO : IFileSupplementalMetadataRequest
{
    public string? FileUrl { get; set; }

    public Guid AccessGroupId { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? Categories { get; set; }
    public string? Description { get; set; }
    public bool PublicViewing { get; set; }
    public bool PublicDownload { get; set; }
}
