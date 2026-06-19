using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class FileMetadataPolicyTests
{
    [TestCase("quarterly/report")]
    [TestCase("quarterly\\report")]
    public void NormalizeFileName_RejectsPathSeparators(string fileName)
    {
        Action act = () => FileMetadataPolicy.NormalizeFileName(fileName);

        act.Should().Throw<FileMetadataValidationException>()
            .WithMessage("FileName contains invalid characters.");
    }
}
