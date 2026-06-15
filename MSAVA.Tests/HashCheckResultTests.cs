using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class HashCheckResultTests
{
    [Test]
    public void Failed_DoesNotRequireUpload()
    {
        var result = HashCheckResult.Failed("ABC123", "Invalid hash format.");

        result.FileExists.Should().BeFalse();
        result.UploadRequired.Should().BeFalse();
        result.Error.Should().Be("Invalid hash format.");
    }

    [Test]
    public void NotFound_RequiresUpload()
    {
        var result = HashCheckResult.NotFound("ABC123");

        result.FileExists.Should().BeFalse();
        result.UploadRequired.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Test]
    public void ExistingAccess_DoesNotRequireUpload()
    {
        var result = HashCheckResult.ExistingAccess("ABC123", Guid.NewGuid());

        result.FileExists.Should().BeTrue();
        result.UploadRequired.Should().BeFalse();
        result.Error.Should().BeNull();
    }
}
