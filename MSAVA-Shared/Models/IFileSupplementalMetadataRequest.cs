namespace MSAVA_Shared.Models;

public interface IFileSupplementalMetadataRequest
{
    List<string>? Tags { get; set; }

    List<string>? Categories { get; set; }

    string? Description { get; set; }
}
