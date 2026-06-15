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
        ArgumentNullException.ThrowIfNull(password);
        EnsureExpectedSalt(salt);

        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            HashIterations,
            HashAlgorithmName.SHA256,
            HashByteSize);
    }

    public static bool VerifyPassword(string password, byte[] storedHash, byte[] storedSalt)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (!HasExpectedLength(storedHash, HashByteSize) || !HasExpectedLength(storedSalt, SaltSize))
            return false;

        var hash = HashPassword(password, storedSalt);
        return CryptographicOperations.FixedTimeEquals(hash, storedHash);
    }

    private static void EnsureExpectedSalt(byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(salt);

        if (salt.Length != SaltSize)
            throw new ArgumentException($"Password salt must be {SaltSize} bytes.", nameof(salt));
    }

    private static bool HasExpectedLength(byte[]? bytes, int expectedLength)
    {
        return bytes?.Length == expectedLength;
    }
}
