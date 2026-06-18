using System.IO.Compression;
using System.Text;
using MSAVA_BLL.Utils;
using MSAVA_BLL.Utils.Signature;

namespace MSAVA_App.Tests;

public class SignatureDetectorTests
{
    private const string ValidSignature =
        "MSAVA:v1:000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f:42:1700000000";

    [Test]
    public void Detect_ReturnsNullAndResetsStreamWhenOfficeDetectorFails()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a zip archive"));
        stream.Position = 4;

        var signature = SignatureDetector.Detect(stream, "docx");

        signature.Should().BeNull();
        stream.Position.Should().Be(0);
    }

    [Test]
    public void Detect_ReturnsNullAndResetsStreamWhenTagLibDetectorFails()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not an mp3"));
        stream.Position = 3;

        var signature = SignatureDetector.Detect(stream, "mp3");

        signature.Should().BeNull();
        stream.Position.Should().Be(0);
    }

    [Test]
    public void Detect_ReturnsNullAndResetsStreamWhenSvgContainsDtd()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"""
            <!DOCTYPE svg [
              <!ENTITY expansion "expanded">
            ]>
            <svg xmlns="http://www.w3.org/2000/svg">
              <metadata><!-- {ValidSignature} --></metadata>
              <text>&expansion;</text>
            </svg>
            """));
        stream.Position = 12;

        var signature = SignatureDetector.Detect(stream, "svg");

        signature.Should().BeNull();
        stream.Position.Should().Be(0);
    }

    [Test]
    public void Detect_ReturnsNullAndResetsStreamWhenSvgzInflatesPastXmlLimit()
    {
        using var stream = CreateOversizedSvgz();
        stream.Position = 12;

        var signature = SignatureDetector.Detect(stream, "svgz");

        signature.Should().BeNull();
        stream.Position.Should().Be(0);
    }

    [Test]
    public void Detect_FindsPdfSignatureFromNonSeekableStreamWithoutLargeReadBuffer()
    {
        using var stream = new BufferLimitedReadStream(
            Encoding.ASCII.GetBytes($"""
                %PDF-1.7
                {new string('x', 12_000)}
                % {ValidSignature}
                """),
            maximumReadSize: 4096);

        var signature = SignatureDetector.Detect(stream, "pdf");

        signature.Should().NotBeNull();
        signature!.ContentHash.Should().Be("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");
        signature.FileId.Should().Be(42);
        signature.Timestamp.Should().Be(1700000000);
        stream.LargestRequestedReadSize.Should().BeLessThanOrEqualTo(4096);
    }

    [Test]
    public void Detect_PropagatesCriticalDetectorFailure()
    {
        using var stream = new ThrowingReadStream(new OutOfMemoryException("Critical detector failure."));

        Action act = () => SignatureDetector.Detect(stream, "svg");

        act.Should().Throw<OutOfMemoryException>()
            .WithMessage("Critical detector failure.");
    }

    private sealed class ThrowingReadStream(Exception exception) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
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

    private sealed class BufferLimitedReadStream(byte[] content, int maximumReadSize) : Stream
    {
        private int _position;

        public int LargestRequestedReadSize { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            LargestRequestedReadSize = Math.Max(LargestRequestedReadSize, count);

            if (count > maximumReadSize)
                throw new InvalidOperationException("Read buffer is larger than the stream boundary allows.");

            if (_position >= content.Length)
                return 0;

            var bytesToRead = Math.Min(count, content.Length - _position);
            Array.Copy(content, _position, buffer, offset, bytesToRead);
            _position += bytesToRead;
            return bytesToRead;
        }

        public override int Read(Span<byte> buffer)
        {
            LargestRequestedReadSize = Math.Max(LargestRequestedReadSize, buffer.Length);

            if (buffer.Length > maximumReadSize)
                throw new InvalidOperationException("Read buffer is larger than the stream boundary allows.");

            if (_position >= content.Length)
                return 0;

            var bytesToRead = Math.Min(buffer.Length, content.Length - _position);
            content.AsSpan(_position, bytesToRead).CopyTo(buffer);
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

    private static MemoryStream CreateOversizedSvgz()
    {
        var builder = new StringBuilder((int)SafeXmlDocumentLoader.MaximumXmlCharacters + 4096);
        builder.Append("""<svg xmlns="http://www.w3.org/2000/svg"><metadata><!-- """);
        builder.Append(ValidSignature);
        builder.Append(""" --></metadata><text>""");
        builder.Append('x', (int)SafeXmlDocumentLoader.MaximumXmlCharacters);
        builder.Append("</text></svg>");

        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, Encoding.UTF8))
        {
            writer.Write(builder.ToString());
        }

        output.Position = 0;
        return output;
    }
}
