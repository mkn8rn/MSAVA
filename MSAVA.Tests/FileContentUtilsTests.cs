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
}
