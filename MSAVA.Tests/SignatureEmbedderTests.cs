using System.Text;
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

    private static MsavaSignature CreateSignature()
    {
        return new MsavaSignature
        {
            ContentHash = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f",
            FileId = 42,
            Timestamp = 1700000000
        };
    }
}
