using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class AuthenticationCredentialPolicyTests
{
    [Test]
    public void CreateUsernameComparisonKey_UsesInvariantUppercase()
    {
        AuthenticationCredentialPolicy.CreateUsernameComparisonKey("admin")
            .Should()
            .Be("ADMIN");
    }

    [Test]
    public void CreateUsernameComparisonKey_RejectsMissingUsername()
    {
        Action act = () => AuthenticationCredentialPolicy.CreateUsernameComparisonKey(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("username");
    }
}
