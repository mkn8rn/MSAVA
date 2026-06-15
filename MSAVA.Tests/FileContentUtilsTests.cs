using MSAVA_INF.Utils;

namespace MSAVA_App.Tests;

public class FileContentUtilsTests
{
    [Test]
    public void FileContentUtils_DoesNotExposeUnsafeStringPathBuilder()
    {
        var unsafeOverload = typeof(FileContentUtils)
            .GetMethods()
            .SingleOrDefault(method =>
                method.Name == nameof(FileContentUtils.GetFullPath) &&
                method.GetParameters() is [{ ParameterType: var parameterType }] &&
                parameterType == typeof(string));

        unsafeOverload.Should().BeNull();
    }

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
    public void IsSafeFilePath_RejectsPathWithNullCharacter()
    {
        var path = Path.Combine(FileContentUtils.FilesDirectory, "bad\0file.txt");

        FileContentUtils.IsSafeFilePath(path).Should().BeFalse();
    }

    [TestCase("txt")]
    [TestCase(".TXT")]
    [TestCase("_TXT")]
    [TestCase("  .Txt  ")]
    public void GetFullPath_NormalizesExtensionPrefixAndCasing(string extension)
    {
        byte[] hash =
        [
            0x00, 0x01, 0x02, 0x03,
            0x04, 0x05, 0x06, 0x07,
            0x08, 0x09, 0x0A, 0x0B,
            0x0C, 0x0D, 0x0E, 0x0F,
            0x10, 0x11, 0x12, 0x13,
            0x14, 0x15, 0x16, 0x17,
            0x18, 0x19, 0x1A, 0x1B,
            0x1C, 0x1D, 0x1E, 0x1F
        ];

        var path = FileContentUtils.GetFullPath(hash, extension);

        path.Should().Be(Path.Combine(
            FileContentUtils.FilesDirectory,
            "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f.txt"));
    }

    [Test]
    public void TryGetSafeFullPath_ReturnsPathForSafeStoredFileNameWithoutRequiringFileExists()
    {
        string fileName = $"{new string('a', 64)}.txt";

        bool success = FileContentUtils.TryGetSafeFullPath(fileName, out string fullPath);

        success.Should().BeTrue();
        fullPath.Should().Be(Path.Combine(FileContentUtils.FilesDirectory, fileName));
    }

    [TestCase("../secret.txt")]
    [TestCase("folder/file.txt")]
    [TestCase("")]
    [TestCase(" ")]
    public void TryGetSafeFullPath_RejectsUnsafeStoredFileName(string fileName)
    {
        bool success = FileContentUtils.TryGetSafeFullPath(fileName, out string fullPath);

        success.Should().BeFalse();
        fullPath.Should().BeEmpty();
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
