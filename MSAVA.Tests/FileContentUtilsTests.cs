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
}
