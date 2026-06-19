namespace MSAVA_Shared.Models;

public class SaveFileFromFetchDTO : IFileCreationMetadataRequest
{
    public required string FileName { get; set; }
    public required string FileExtension { get; set; }
    public required string TempFilePath { get; set; }
    public required Guid AccessGroupId { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? Categories { get; set; }
    public string? Description { get; set; }
    public bool PublicViewing { get; set; } = false;
    public bool PublicDownload { get; set; } = false;
}
