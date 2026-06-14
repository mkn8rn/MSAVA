using System.Text;
using MSAVA_BLL.Utils.Metadata;

namespace MSAVA_App.Tests;

public class DocumentMetadataExtractorTests
{
    [Test]
    public void ExtractMetadata_NormalizesValidPdfCreationDate()
    {
        using var stream = CreatePdfWithCreationDate("D:20240613091522");

        using var metadata = MetadataExtractor.ExtractMetadata(stream, "pdf", stream.Length);

        metadata.RootElement.GetProperty("CreationDate").GetString()
            .Should().Be("2024-06-13");
    }

    [Test]
    public void ExtractMetadata_PreservesMalformedPdfCreationDate()
    {
        using var stream = CreatePdfWithCreationDate("D:20241340");

        using var metadata = MetadataExtractor.ExtractMetadata(stream, ".pdf", stream.Length);

        metadata.RootElement.GetProperty("CreationDate").GetString()
            .Should().Be("D:20241340");
    }

    private static MemoryStream CreatePdfWithCreationDate(string creationDate)
    {
        var content = $"""
            %PDF-1.7
            1 0 obj
            << /Type /Page >>
            endobj
            2 0 obj
            << /CreationDate ({creationDate}) >>
            endobj
            """;

        return new MemoryStream(Encoding.Latin1.GetBytes(content));
    }
}
