namespace MSAVA_BLL.Services.Auth;

internal static class AuthTextInputPolicy
{
    public static bool ContainsControlCharacter(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        foreach (char character in value)
        {
            if (char.IsControl(character))
                return true;
        }

        return false;
    }
}
