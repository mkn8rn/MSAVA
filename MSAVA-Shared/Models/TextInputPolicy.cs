namespace MSAVA_Shared.Models;

public static class TextInputPolicy
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
