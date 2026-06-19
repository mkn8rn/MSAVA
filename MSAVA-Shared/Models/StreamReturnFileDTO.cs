namespace MSAVA_Shared.Models;

public class StreamReturnFileDTO
{
    public Guid Id { get; set; }
    public required string FileName { get; set; }
    public required string FileExtension { get; set; }
    public required Stream FileStream { get; set; }

    public string DownloadFileName
    {
        get
        {
            string normalizedFileName = FileMetadataPolicy.NormalizeFileName(FileName);
            string normalizedExtension = FileMetadataPolicy.NormalizeFileExtensionSyntax(FileExtension);
            string expectedSuffix = $".{normalizedExtension}";

            return normalizedFileName.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase)
                ? normalizedFileName
                : $"{normalizedFileName}{expectedSuffix}";
        }
    }
}
