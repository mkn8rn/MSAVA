using System.Text;
using MSAVA_BLL.Utils.Signature;

namespace MSAVA_App.Tests;

public class SignatureDetectorTests
{
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
}
