using System.Text;
using MSAVA_BLL.Utils.Signature;

namespace MSAVA_App.Tests;

public class MsavaSignatureTests
{
    private const string ContentHash = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.FromUnixTimeSeconds(1700000123);

    [Test]
    public void Create_UsesProvidedTimestamp()
    {
        var signature = MsavaSignature.Create(ContentHash, fileId: 42, createdAt: FixedNow);

        signature.ContentHash.Should().Be(ContentHash);
        signature.FileId.Should().Be(42);
        signature.Timestamp.Should().Be(FixedNow.ToUnixTimeSeconds());
    }

    [Test]
    public void GetAge_UsesProvidedCurrentTime()
    {
        var signature = new MsavaSignature
        {
            ContentHash = ContentHash,
            FileId = 42,
            Timestamp = FixedNow.ToUnixTimeSeconds()
        };

        var age = signature.GetAge(FixedNow.AddMinutes(5));

        age.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void SignatureService_UsesInjectedClockWhenPreparingDownload()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><title>MSAVA</title></svg>"));
        var service = new SignatureService(new FixedTimeProvider(FixedNow));

        using var prepared = service.PrepareForDownload(stream, "svg", ContentHash, fileId: 42);
        var result = SignatureDetector.TryDetect(prepared, "svg");

        result.Found.Should().BeTrue();
        result.Signature.Should().NotBeNull();
        result.Signature!.Timestamp.Should().Be(FixedNow.ToUnixTimeSeconds());
    }

    [Test]
    public void TryParse_AcceptsSha256HexContentHash()
    {
        const string contentHash = "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F";
        string input = $"MSAVA:v1:{contentHash}:42:1700000000";

        bool parsed = MsavaSignature.TryParse(input, out var signature);

        parsed.Should().BeTrue();
        signature.Should().NotBeNull();
        signature!.ContentHash.Should().Be(contentHash);
        signature.FileId.Should().Be(42);
        signature.Timestamp.Should().Be(1700000000);
    }

    [Test]
    public void TryParse_RejectsNonHexContentHash()
    {
        const string contentHash = "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz";
        string input = $"MSAVA:v1:{contentHash}:42:1700000000";

        bool parsed = MsavaSignature.TryParse(input, out var signature);

        parsed.Should().BeFalse();
        signature.Should().BeNull();
    }

    [Test]
    public void TryParse_RejectsShortContentHash()
    {
        string input = "MSAVA:v1:abc:42:1700000000";

        bool parsed = MsavaSignature.TryParse(input, out var signature);

        parsed.Should().BeFalse();
        signature.Should().BeNull();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
