namespace MSAVA_Shared.Models;

public interface IFileCreationMetadataRequest
{
    string FileName { get; set; }

    string FileExtension { get; set; }

    List<string>? Tags { get; set; }

    List<string>? Categories { get; set; }

    string? Description { get; set; }
}
