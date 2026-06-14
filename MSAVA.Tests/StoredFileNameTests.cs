using MSAVA_INF.Utils;

namespace MSAVA_App.Tests;

public class StoredFileNameTests
{
    [Test]
    public void TryParse_ReturnsHashHexBytesAndLowercaseExtension()
    {
        const string hashHex = "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F";
        string path = Path.Combine(Path.GetTempPath(), $"{hashHex}.TXT");

        bool parsed = StoredFileName.TryParse(path, out var storedFileName);

        parsed.Should().BeTrue();
        storedFileName.FileHashHex.Should().Be(hashHex);
        storedFileName.FileHash.Should().Equal(
            [
                0x00, 0x01, 0x02, 0x03,
                0x04, 0x05, 0x06, 0x07,
                0x08, 0x09, 0x0A, 0x0B,
                0x0C, 0x0D, 0x0E, 0x0F,
                0x10, 0x11, 0x12, 0x13,
                0x14, 0x15, 0x16, 0x17,
                0x18, 0x19, 0x1A, 0x1B,
                0x1C, 0x1D, 0x1E, 0x1F
            ]);
        storedFileName.Extension.Should().Be("txt");
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("abc.txt")]
    [TestCase("000102030405060708090a0b0c0d0e0f.txt")]
    [TestCase("000102030405060708090g0b0c0d0e0f101112131415161718191a1b1c1d1e1f.txt")]
    [TestCase("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f")]
    [TestCase("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f.")]
    public void TryParse_RejectsMalformedStoredFileNames(string fileName)
    {
        bool parsed = StoredFileName.TryParse(fileName, out var storedFileName);

        parsed.Should().BeFalse();
        storedFileName.Should().Be(default(StoredFileName));
    }
}
