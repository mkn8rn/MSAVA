using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class HashCheckResultTests
{
    [Test]
    public void RequestPolicy_RejectsMissingRequest()
    {
        bool isValid = HashCheckRequestPolicy.TryValidate(null, out var failureResult);

        isValid.Should().BeFalse();
        failureResult.Should().BeEquivalentTo(HashCheckResult.Failed("", HashCheckRequestPolicy.RequiredMessage));
    }

    [Test]
    public void RequestPolicy_AcceptsProvidedRequest()
    {
        var request = CreateRequest(0);

        bool isValid = HashCheckRequestPolicy.TryValidate(request, out var failureResult);

        isValid.Should().BeTrue();
        failureResult.Should().BeNull();
    }

    [Test]
    public void BatchPolicy_RejectsMissingRequestList()
    {
        bool isValid = HashCheckBatchPolicy.TryValidate(null, out var failureResults);

        isValid.Should().BeFalse();
        failureResults.Should().BeEquivalentTo(
            [HashCheckResult.Failed("", HashCheckBatchPolicy.RequiredMessage)]);
    }

    [Test]
    public void BatchPolicy_RejectsTooManyRequests()
    {
        var requests = Enumerable.Range(0, HashCheckBatchPolicy.MaximumRequestCount + 1)
            .Select(CreateRequest)
            .ToList();

        bool isValid = HashCheckBatchPolicy.TryValidate(requests, out var failureResults);

        isValid.Should().BeFalse();
        failureResults.Should().BeEquivalentTo(
            [HashCheckResult.Failed("", HashCheckBatchPolicy.MaximumRequestCountMessage)]);
    }

    [Test]
    public void BatchPolicy_AcceptsRequestListAtMaximumCount()
    {
        var requests = Enumerable.Range(0, HashCheckBatchPolicy.MaximumRequestCount)
            .Select(CreateRequest)
            .ToList();

        bool isValid = HashCheckBatchPolicy.TryValidate(requests, out var failureResults);

        isValid.Should().BeTrue();
        failureResults.Should().BeEmpty();
    }

    [Test]
    public void Failed_DoesNotRequireUpload()
    {
        var result = HashCheckResult.Failed("ABC123", "Invalid hash format.");

        result.FileExists.Should().BeFalse();
        result.UploadRequired.Should().BeFalse();
        result.Error.Should().Be("Invalid hash format.");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void Failed_RejectsMissingError(string? error)
    {
        Action act = () => HashCheckResult.Failed("ABC123", error!);

        var exception = act.Should().Throw<ArgumentException>().Which;
        exception.ParamName.Should().Be("error");
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

    private static HashCheckRequest CreateRequest(int index)
    {
        return new HashCheckRequest
        {
            ContentHashHex = index.ToString("x64"),
            FileExtension = "txt"
        };
    }
}
