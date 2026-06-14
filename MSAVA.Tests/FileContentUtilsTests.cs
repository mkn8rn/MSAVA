using MSAVA_INF.Utils;

namespace MSAVA_App.Tests;

public class FileContentUtilsTests
{
    [Test]
    public void IsSafeFilePath_AllowsFileInsideFilesDirectory()
    {
        var path = Path.Combine(FileContentUtils.FilesDirectory, "file.txt");

        FileContentUtils.IsSafeFilePath(path).Should().BeTrue();
    }

    [Test]
    public void IsSafeFilePath_RejectsSiblingDirectoryWithMatchingPrefix()
    {
        var path = Path.Combine($"{FileContentUtils.FilesDirectory}-evil", "file.txt");

        FileContentUtils.IsSafeFilePath(path).Should().BeFalse();
    }

    [Test]
    public void IsSafeFilePath_RejectsTraversalThatResolvesOutsideFilesDirectory()
    {
        var path = Path.Combine(FileContentUtils.FilesDirectory, "..", "outside.txt");

        FileContentUtils.IsSafeFilePath(path).Should().BeFalse();
    }

    [Test]
    public void IsSafeFilePath_RejectsBlankPath()
    {
        FileContentUtils.IsSafeFilePath(" ").Should().BeFalse();
    }

    [Test]
    public void ValidateFileContent_AcceptsPdfHeaderAndRestoresStreamPosition()
    {
        using var stream = new MemoryStream([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31]);
        stream.Position = 2;

        bool valid = FileContentUtils.ValidateFileContent(stream, ".pdf");

        valid.Should().BeTrue();
        stream.Position.Should().Be(2);
    }

    [Test]
    public void ValidateFileContent_RejectsBlankExtension()
    {
        using var stream = new MemoryStream([0x25, 0x50, 0x44, 0x46]);

        FileContentUtils.ValidateFileContent(stream, " ").Should().BeFalse();
    }

    [Test]
    public void ValidateFileContent_AcceptsSvgRootWithoutXmlDeclaration()
    {
        using var stream = new MemoryStream("<svg viewBox=\"0 0 1 1\"></svg>"u8.ToArray());

        FileContentUtils.ValidateFileContent(stream, "svg").Should().BeTrue();
    }

    [Test]
    public void ValidateFileContent_AcceptsSvgXmlDeclaration()
    {
        using var stream = new MemoryStream("<?xml version=\"1.0\"?><svg></svg>"u8.ToArray());

        FileContentUtils.ValidateFileContent(stream, ".svg").Should().BeTrue();
    }

    [Test]
    public void ValidateFileContent_AcceptsSvgzGzipHeader()
    {
        using var stream = new MemoryStream([0x1F, 0x8B, 0x08, 0x00]);

        FileContentUtils.ValidateFileContent(stream, "svgz").Should().BeTrue();
    }

    [Test]
    public void ValidateFileContent_RejectsPlainSvgAsSvgz()
    {
        using var stream = new MemoryStream("<svg></svg>"u8.ToArray());

        FileContentUtils.ValidateFileContent(stream, "svgz").Should().BeFalse();
    }
}
