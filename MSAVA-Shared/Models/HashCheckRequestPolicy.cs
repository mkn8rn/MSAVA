using System.Diagnostics.CodeAnalysis;

namespace MSAVA_Shared.Models;

public static class HashCheckRequestPolicy
{
    public const string RequiredMessage = "Hash check request is required.";

    public static bool TryValidate(
        [NotNullWhen(true)]
        HashCheckRequest? request,
        [NotNullWhen(false)]
        out HashCheckResult? failureResult)
    {
        if (request is null)
        {
            failureResult = HashCheckResult.Failed("", RequiredMessage);
            return false;
        }

        failureResult = null;
        return true;
    }
}
