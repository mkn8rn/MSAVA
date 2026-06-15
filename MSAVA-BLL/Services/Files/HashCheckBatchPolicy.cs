using System.Diagnostics.CodeAnalysis;
using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Files;

public static class HashCheckBatchPolicy
{
    public const int MaximumRequestCount = 100;
    public const string RequiredMessage = "Hash check batch request is required.";
    public const string MaximumRequestCountMessage = "Maximum 100 hashes per batch request.";

    public static bool TryValidate(
        [NotNullWhen(true)]
        List<HashCheckRequest>? requests,
        out List<HashCheckResult> failureResults)
    {
        if (requests is null)
        {
            failureResults = [HashCheckResult.Failed("", RequiredMessage)];
            return false;
        }

        if (requests.Count > MaximumRequestCount)
        {
            failureResults = [HashCheckResult.Failed("", MaximumRequestCountMessage)];
            return false;
        }

        failureResults = [];
        return true;
    }
}
