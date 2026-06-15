using MSAVA_BLL.Utils;

namespace MSAVA_App.Tests;

public class PasswordUtilsTests
{
    [Test]
    public void VerifyPassword_ReturnsTrueForMatchingPassword()
    {
        byte[] salt = PasswordUtils.GenerateSalt();
        byte[] hash = PasswordUtils.HashPassword("correct-password", salt);

        bool result = PasswordUtils.VerifyPassword("correct-password", hash, salt);

        result.Should().BeTrue();
    }

    [Test]
    public void VerifyPassword_ReturnsFalseForWrongPassword()
    {
        byte[] salt = PasswordUtils.GenerateSalt();
        byte[] hash = PasswordUtils.HashPassword("correct-password", salt);

        bool result = PasswordUtils.VerifyPassword("wrong-password", hash, salt);

        result.Should().BeFalse();
    }

    [Test]
    public void VerifyPassword_ReturnsFalseForMalformedStoredHashOrSalt()
    {
        byte[] salt = PasswordUtils.GenerateSalt();
        byte[] hash = PasswordUtils.HashPassword("correct-password", salt);

        PasswordUtils.VerifyPassword("correct-password", [1], salt)
            .Should()
            .BeFalse();

        PasswordUtils.VerifyPassword("correct-password", hash, [1])
            .Should()
            .BeFalse();
    }

    [Test]
    public void VerifyPassword_ReturnsFalseForMissingStoredHashOrSalt()
    {
        byte[] salt = PasswordUtils.GenerateSalt();
        byte[] hash = PasswordUtils.HashPassword("correct-password", salt);

        PasswordUtils.VerifyPassword("correct-password", null!, salt)
            .Should()
            .BeFalse();

        PasswordUtils.VerifyPassword("correct-password", hash, null!)
            .Should()
            .BeFalse();
    }

    [Test]
    public void HashPassword_RejectsUnexpectedSaltShape()
    {
        Action missingSalt = () => PasswordUtils.HashPassword("correct-password", null!);
        Action malformedSalt = () => PasswordUtils.HashPassword("correct-password", [1]);

        missingSalt.Should().Throw<ArgumentNullException>()
            .WithParameterName("salt");

        malformedSalt.Should().Throw<ArgumentException>()
            .WithMessage("Password salt must be 32 bytes.*")
            .WithParameterName("salt");
    }

    [Test]
    public void HashPassword_RejectsMissingPassword()
    {
        byte[] salt = PasswordUtils.GenerateSalt();

        Action act = () => PasswordUtils.HashPassword(null!, salt);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("password");
    }
}
