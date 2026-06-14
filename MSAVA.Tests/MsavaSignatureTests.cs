using MSAVA_BLL.Utils.Signature;

namespace MSAVA_App.Tests;

public class MsavaSignatureTests
{
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
}
