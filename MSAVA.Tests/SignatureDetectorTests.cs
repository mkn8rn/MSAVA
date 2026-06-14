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
}
