using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class FileSizePolicyTests
{
    [Test]
    public void RequireValidMaximum_ReturnsPositiveMaximum()
    {
        FileSizePolicy.RequireValidMaximum(1).Should().Be(1);
    }

    [Test]
    public void RequireValidMaximum_RejectsZeroMaximum()
    {
        Action act = () => FileSizePolicy.RequireValidMaximum(0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("maximumFileSizeBytes");
    }

    [Test]
    public void EnsureWithinMaximum_RejectsOversizedFile()
    {
        Action act = () => FileSizePolicy.EnsureWithinMaximum(101, 100);

        act.Should().Throw<FileTooLargeException>()
            .Where(exception =>
                exception.FileSizeBytes == 101 &&
                exception.MaximumFileSizeBytes == 100);
    }

    [Test]
    public void EnsureChunkWithinMaximum_RejectsChunkThatCrossesMaximum()
    {
        Action act = () => FileSizePolicy.EnsureChunkWithinMaximum(90, 11, 100);

        act.Should().Throw<FileTooLargeException>()
            .Where(exception =>
                exception.FileSizeBytes == 101 &&
                exception.MaximumFileSizeBytes == 100);
    }
}
