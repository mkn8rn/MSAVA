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
        metadata.RootElement.GetProperty("AnalysisTruncated").GetBoolean().Should().BeFalse();
    }

    [Test]
    public void ExtractMetadata_PreservesMalformedPdfCreationDate()
    {
        using var stream = CreatePdfWithCreationDate("D:20241340");

        using var metadata = MetadataExtractor.ExtractMetadata(stream, ".pdf", stream.Length);

        metadata.RootElement.GetProperty("CreationDate").GetString()
            .Should().Be("D:20241340");
    }

    [Test]
    public void ExtractMetadata_BoundsLargePdfAnalysis()
    {
        using var stream = CreateLargePdfWithCreationDate("D:20240613091522");

        using var metadata = MetadataExtractor.ExtractMetadata(stream, "pdf", stream.Length);

        metadata.RootElement.GetProperty("CreationDate").GetString()
            .Should().Be("2024-06-13");
        metadata.RootElement.GetProperty("AnalysisTruncated").GetBoolean().Should().BeTrue();
        stream.Position.Should().BeLessThan(stream.Length);
    }

    [Test]
    public void ExtractMetadata_ReturnsInvalidMetadataForUnreadableLegacyExcel()
    {
        using var stream = new MemoryStream("not an xls workbook"u8.ToArray());

        using var metadata = MetadataExtractor.ExtractMetadata(stream, "xls", 123);

        metadata.RootElement.GetProperty("Valid").GetBoolean().Should().BeFalse();
        metadata.RootElement.GetProperty("Reason").GetString()
            .Should().Be("Failed to read Excel file");
        metadata.RootElement.GetProperty("Size").GetInt64().Should().Be(123);
    }

    [Test]
    public void ExtractMetadata_PropagatesCriticalLegacyExcelFailure()
    {
        using var stream = new ThrowingReadStream(new OutOfMemoryException("Critical read failure."));

        Action act = () => MetadataExtractor.ExtractMetadata(stream, "xls", 123);

        act.Should().Throw<OutOfMemoryException>()
            .WithMessage("Critical read failure.");
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

    private static MemoryStream CreateLargePdfWithCreationDate(string creationDate)
    {
        var builder = new StringBuilder(BoundedMetadataTextReader.MaximumAnalyzedCharacters + 50_000);
        builder.AppendLine("%PDF-1.7");
        builder.AppendLine("1 0 obj");
        builder.AppendLine("<< /Type /Page >>");
        builder.AppendLine("endobj");
        builder.AppendLine("2 0 obj");
        builder.AppendLine($"<< /CreationDate ({creationDate}) >>");
        builder.AppendLine("endobj");
        builder.Append('x', BoundedMetadataTextReader.MaximumAnalyzedCharacters + 50_000);

        return new MemoryStream(Encoding.Latin1.GetBytes(builder.ToString()));
    }

    private sealed class ThrowingReadStream(Exception exception) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => 0;

        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw exception;
        }

        public override int Read(Span<byte> buffer)
        {
            throw exception;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => Length + offset,
                _ => Position
            };

            return Position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
