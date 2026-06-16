using MSAVA_Shared.Diagnostics;

namespace MSAVA_App.Tests;

public class CriticalExceptionPolicyTests
{
    [Test]
    public void ContainsCriticalException_ReturnsTrueForWrappedCancellation()
    {
        var exception = new InvalidOperationException(
            "Request pipeline failed.",
            new OperationCanceledException("Request was canceled."));

        CriticalExceptionPolicy.ContainsCriticalException(exception).Should().BeTrue();
    }

    [Test]
    public void ContainsCriticalException_ReturnsTrueForWrappedNonCancellationCriticalFailure()
    {
        var exception = new InvalidOperationException(
            "Metadata extraction failed.",
            new AccessViolationException("Native boundary failed."));

        CriticalExceptionPolicy.ContainsCriticalException(exception).Should().BeTrue();
    }

    [Test]
    public void ContainsCriticalException_ReturnsFalseForRecoverableException()
    {
        var exception = new InvalidDataException("Unsupported metadata shape.");

        CriticalExceptionPolicy.ContainsCriticalException(exception).Should().BeFalse();
    }

    [Test]
    public void ContainsNonCancellationCriticalException_ExcludesCancellation()
    {
        var exception = new InvalidOperationException(
            "Login request failed.",
            new OperationCanceledException("Request was canceled."));

        CriticalExceptionPolicy.ContainsNonCancellationCriticalException(exception).Should().BeFalse();
    }

    [Test]
    public void ContainsNonCancellationCriticalException_ReturnsTrueForWrappedOutOfMemory()
    {
        var exception = new InvalidOperationException(
            "Response parsing failed.",
            new OutOfMemoryException("Memory pressure."));

        CriticalExceptionPolicy.ContainsNonCancellationCriticalException(exception).Should().BeTrue();
    }

    [Test]
    public void ContainsCriticalException_RejectsMissingException()
    {
        Action act = () => CriticalExceptionPolicy.ContainsCriticalException(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("exception");
    }
}
