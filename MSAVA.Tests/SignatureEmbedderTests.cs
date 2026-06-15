using System.Text;
using System.IO.Compression;
using MSAVA_BLL.Utils.Signature;

namespace MSAVA_App.Tests;

public class SignatureEmbedderTests
{
    [Test]
    public void Embed_ThrowsWhenTagLibCannotWriteSignature()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not an mp3"));
        var signature = CreateSignature();

        Action act = () =>
        {
            using var _ = SignatureEmbedder.Embed(stream, "mp3", signature);
        };

        act.Should().Throw<Exception>();
    }

    [Test]
    public void TryEmbed_ReturnsOriginalStreamAndResetsPositionWhenTagLibCannotWriteSignature()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not an mp3"));
        stream.Position = 3;
        var signature = CreateSignature();

        var result = SignatureEmbedder.TryEmbed(stream, "mp3", signature);

        result.Should().BeSameAs(stream);
        stream.Position.Should().Be(0);
    }

    [Test]
    public void TryEmbed_PropagatesCriticalEmbeddingFailure()
    {
        using var stream = new ThrowingReadStream(new OutOfMemoryException("Critical embed failure."));
        var signature = CreateSignature();

        Action act = () => SignatureEmbedder.TryEmbed(stream, "svg", signature);

        act.Should().Throw<OutOfMemoryException>()
            .WithMessage("Critical embed failure.");
    }

    [Test]
    public void Embed_ReplacesMalformedOfficeCustomProperties()
    {
        using var document = CreateOfficeDocumentWithMalformedCustomProperties();
        var signature = CreateSignature();

        using var embedded = SignatureEmbedder.Embed(document, "docx", signature);

        using var archive = new ZipArchive(embedded, ZipArchiveMode.Read, leaveOpen: true);
        var customProperties = archive.GetEntry("docProps/custom.xml");
        customProperties.Should().NotBeNull();
        using var entryStream = customProperties!.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        var customXml = reader.ReadToEnd();
        customXml.Should().Contain(MsavaSignature.MetadataKey);
        customXml.Should().Contain(signature.ToString());
    }

    private static MsavaSignature CreateSignature()
    {
        return new MsavaSignature
        {
            ContentHash = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f",
            FileId = 42,
            Timestamp = 1700000000
        };
    }

    private static MemoryStream CreateOfficeDocumentWithMalformedCustomProperties()
    {
        var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var customProperties = archive.CreateEntry("docProps/custom.xml");
            using var entryStream = customProperties.Open();
            using var writer = new StreamWriter(entryStream, Encoding.UTF8);
            writer.Write("<Properties><property>");
        }

        stream.Position = 0;
        return stream;
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
