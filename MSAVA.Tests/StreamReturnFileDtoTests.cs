using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class StreamReturnFileDtoTests
{
    [Test]
    public void DownloadFileName_AddsDotBetweenNameAndExtension()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var dto = new StreamReturnFileDTO
        {
            FileName = "abc123",
            FileExtension = "pdf",
            FileStream = stream
        };

        dto.DownloadFileName.Should().Be("abc123.pdf");
    }

    [Test]
    public void DownloadFileName_DoesNotDuplicateExistingExtension()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var dto = new StreamReturnFileDTO
        {
            FileName = "abc123.pdf",
            FileExtension = "pdf",
            FileStream = stream
        };

        dto.DownloadFileName.Should().Be("abc123.pdf");
    }

    [Test]
    public void DownloadFileName_NormalizesExtensionWithLeadingDot()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var dto = new StreamReturnFileDTO
        {
            FileName = "abc123",
            FileExtension = ".pdf",
            FileStream = stream
        };

        dto.DownloadFileName.Should().Be("abc123.pdf");
    }

    [Test]
    public void DownloadFileName_NormalizesExtensionCasingAndPrefix()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var dto = new StreamReturnFileDTO
        {
            FileName = "abc123",
            FileExtension = " _PDF ",
            FileStream = stream
        };

        dto.DownloadFileName.Should().Be("abc123.pdf");
    }

    [Test]
    public void DownloadFileName_RejectsPathLikeFileName()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var dto = new StreamReturnFileDTO
        {
            FileName = "quarterly/report",
            FileExtension = "pdf",
            FileStream = stream
        };

        Action act = () => _ = dto.DownloadFileName;

        act.Should().Throw<FileMetadataValidationException>()
            .WithMessage("FileName contains invalid characters.");
    }

    [Test]
    public void DownloadFileName_RejectsPathLikeExtension()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var dto = new StreamReturnFileDTO
        {
            FileName = "abc123",
            FileExtension = "folder/pdf",
            FileStream = stream
        };

        Action act = () => _ = dto.DownloadFileName;

        act.Should().Throw<FileMetadataValidationException>()
            .WithMessage("FileExtension contains invalid characters.");
    }
}
