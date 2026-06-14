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
    public void ExtractMetadata_PropagatesOperationCanceledException()
    {
        using var stream = new ThrowingReadStream(new OperationCanceledException("cancelled"));

        Action act = () => MetadataExtractor.ExtractMetadata(stream, "txt", 42);

        act.Should().Throw<OperationCanceledException>();
    }

    [Test]
    public void ExtractMetadata_PropagatesOutOfMemoryException()
    {
        using var stream = new ThrowingReadStream(new OutOfMemoryException("memory pressure"));

        Action act = () => MetadataExtractor.ExtractMetadata(stream, "txt", 42);

        act.Should().Throw<OutOfMemoryException>();
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
}
