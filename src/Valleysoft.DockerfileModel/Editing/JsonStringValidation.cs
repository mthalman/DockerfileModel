namespace Valleysoft.DockerfileModel;

/// <summary>Validates newly adopted JSON string syntax independently of the Dockerfile literal parser.</summary>
internal static class JsonStringValidation
{
    /// <summary>Requires complete physical input to fold into exactly one JSON string, without decoding it.</summary>
    /// <param name="text">The prospective double-quoted physical representation.</param>
    /// <param name="escapeChar">The active Dockerfile continuation character, not a JSON escape character.</param>
    /// <returns>Whether all physical input and all logical JSON string syntax are valid and consumed.</returns>
    internal static bool IsValid(string text, char escapeChar)
    {
        string logical = ConstructReader.FoldHeader(text, 0, escapeChar, out int end);
        if (end != text.Length || logical.Length < 2 || logical[0] != '"')
        {
            return false;
        }

        for (int index = 1; index < logical.Length; index++)
        {
            char current = logical[index];
            if (current == '"')
            {
                return index == logical.Length - 1;
            }
            if (current < ' ')
            {
                return false;
            }
            if (current != '\\')
            {
                continue;
            }
            if (++index >= logical.Length)
            {
                return false;
            }
            switch (logical[index])
            {
                case '"':
                case '\\':
                case '/':
                case 'b':
                case 'f':
                case 'n':
                case 'r':
                case 't':
                    break;
                case 'u':
                    for (int digit = 0; digit < 4; digit++)
                    {
                        if (++index >= logical.Length || !IsHexDigit(logical[index]))
                        {
                            return false;
                        }
                    }
                    break;
                default:
                    return false;
            }
        }
        return false;
    }

    private static bool IsHexDigit(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
