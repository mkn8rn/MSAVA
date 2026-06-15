using MSAVA_API.Authorization;

namespace MSAVA_API.Tests;

public class RecoverableLookupFailurePolicyTests
{
    [Test]
    public void IsRecoverable_AllowsDisposedContextFailuresToBecomeAuthorizationFailures()
    {
        var exception = new ObjectDisposedException("BaseDataContext");

        bool result = RecoverableLookupFailurePolicy.IsRecoverable(exception);

        result.Should().BeTrue();
    }

    [Test]
    public void IsRecoverable_AllowsUnauthorizedAccessFailuresToBecomeAuthorizationFailures()
    {
        var exception = new UnauthorizedAccessException("lookup store denied access");

        bool result = RecoverableLookupFailurePolicy.IsRecoverable(exception);

        result.Should().BeTrue();
    }

    [Test]
    public void IsRecoverable_RejectsCriticalRuntimeFailures()
    {
        var exception = new OutOfMemoryException("critical memory pressure");

        bool result = RecoverableLookupFailurePolicy.IsRecoverable(exception);

        result.Should().BeFalse();
    }

    [Test]
    public void IsRecoverable_RejectsRecoverableWrapperWithCriticalInnerException()
    {
        var exception = new InvalidOperationException(
            "query failed",
            new AccessViolationException("native boundary failed"));

        bool result = RecoverableLookupFailurePolicy.IsRecoverable(exception);

        result.Should().BeFalse();
    }

    [Test]
    public void IsRecoverable_RejectsCancellationFailures()
    {
        var exception = new OperationCanceledException("request was canceled");

        bool result = RecoverableLookupFailurePolicy.IsRecoverable(exception);

        result.Should().BeFalse();
    }
}
