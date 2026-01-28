using System.Security.Cryptography;

namespace MSAVA_BLL.Utils;

public static class PasswordUtils
{
    private const int SaltSize = 32;
    private const int HashIterations = 100000;
    private const int HashByteSize = 32;

    public static byte[] GenerateSalt()
    {
        return RandomNumberGenerator.GetBytes(SaltSize);
    }

    public static byte[] HashPassword(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            HashIterations,
            HashAlgorithmName.SHA256,
            HashByteSize);
    }

    public static bool VerifyPassword(string password, byte[] storedHash, byte[] storedSalt)
    {
        var hash = HashPassword(password, storedSalt);
        return CryptographicOperations.FixedTimeEquals(hash, storedHash);
    }
}
