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
}
