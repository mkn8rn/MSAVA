using System.Text;
using System.Text.Json;
using MSAVA_BLL.Utils.Metadata;

namespace MSAVA_App.Tests;

public class MetadataExtractorTests
{
    [Test]
    public void ExtractMetadata_ReturnsInvalidMetadataForRecoverableReadFailure()
    {
        using var stream = new ThrowingReadStream(new IOException("read failed"));

        using JsonDocument metadata = MetadataExtractor.ExtractMetadata(stream, "txt", 42);

        metadata.RootElement.GetProperty("Valid").GetBoolean().Should().BeFalse();
        metadata.RootElement.GetProperty("Reason").GetString().Should().Be("Extraction failed");
        metadata.RootElement.GetProperty("Size").GetInt64().Should().Be(42);
    }

    [Test]
    public void ExtractMetadata_ReportsExactTextAnalysisForSmallTextFile()
    {
        using var stream = new MemoryStream("one two three"u8.ToArray());

        using JsonDocument metadata = MetadataExtractor.ExtractMetadata(stream, "txt", stream.Length);

        metadata.RootElement.GetProperty("CharacterCount").GetInt32().Should().Be(13);
        metadata.RootElement.GetProperty("WordCount").GetInt32().Should().Be(3);
        metadata.RootElement.GetProperty("AnalysisTruncated").GetBoolean().Should().BeFalse();
    }

    [Test]
    public void ExtractMetadata_BoundsTextAnalysisForLargeTextFile()
    {
        using var stream = new RepeatingByteStream(
            (byte)'a',
            BoundedMetadataTextReader.MaximumAnalyzedCharacters + 50_000);

        using JsonDocument metadata = MetadataExtractor.ExtractMetadata(stream, "txt", stream.Length);

        metadata.RootElement.GetProperty("CharacterCount").GetInt32()
            .Should().Be(BoundedMetadataTextReader.MaximumAnalyzedCharacters);
        metadata.RootElement.GetProperty("AnalysisTruncated").GetBoolean().Should().BeTrue();
        stream.Position.Should().BeLessThan(stream.Length);
    }

    [Test]
    public void ExtractMetadata_RejectsXmlDtdAsInvalidMetadata()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            <!DOCTYPE root [
              <!ENTITY expansion "expanded">
            ]>
            <root>&expansion;</root>
            """));

        using JsonDocument metadata = MetadataExtractor.ExtractMetadata(stream, "xml", stream.Length);

        metadata.RootElement.GetProperty("Valid").GetBoolean().Should().BeFalse();
        metadata.RootElement.GetProperty("Reason").GetString().Should().Be("Extraction failed");
    }

    [Test]
    public void ExtractMetadata_PropagatesOperationCanceledException()
    {
        using var stream = new ThrowingReadStream(new OperationCanceledException("cancelled"));

        Action act = () => MetadataExtractor.ExtractMetadata(stream, "txt", 42);

        act.Should().Throw<OperationCanceledException>();
    }

    [Test]
    public void ExtractMetadata_PropagatesWrappedOperationCanceledException()
    {
        using var stream = new ThrowingReadStream(new InvalidOperationException(
            "wrapped cancellation",
            new OperationCanceledException("cancelled")));

        Action act = () => MetadataExtractor.ExtractMetadata(stream, "txt", 42);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("wrapped cancellation");
    }

    [Test]
    public void ExtractMetadata_PropagatesOutOfMemoryException()
    {
        using var stream = new ThrowingReadStream(new OutOfMemoryException("memory pressure"));

        Action act = () => MetadataExtractor.ExtractMetadata(stream, "txt", 42);

        act.Should().Throw<OutOfMemoryException>();
    }

    [Test]
    public void ExtractMetadata_PropagatesAccessViolationException()
    {
        using var stream = new ThrowingReadStream(new AccessViolationException("native failure"));

        Action act = () => MetadataExtractor.ExtractMetadata(stream, "txt", 42);

        act.Should().Throw<AccessViolationException>()
            .WithMessage("native failure");
    }

    private sealed class ThrowingReadStream(Exception exception) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

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
            throw new NotSupportedException();
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

    private sealed class RepeatingByteStream(byte value, long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= length)
                return 0;

            int bytesToRead = (int)Math.Min(count, length - _position);
            Array.Fill(buffer, value, offset, bytesToRead);
            _position += bytesToRead;
            return bytesToRead;
        }

        public override int Read(Span<byte> buffer)
        {
            if (_position >= length)
                return 0;

            int bytesToRead = (int)Math.Min(buffer.Length, length - _position);
            buffer[..bytesToRead].Fill(value);
            _position += bytesToRead;
            return bytesToRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
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
