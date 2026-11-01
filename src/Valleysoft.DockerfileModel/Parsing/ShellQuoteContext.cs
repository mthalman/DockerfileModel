namespace Valleysoft.DockerfileModel.Parsing;

internal struct ShellQuoteContext
{
    public char? QuoteChar { get; private set; }

    public bool Escaped { get; private set; }

    public bool SuppressExpansion => QuoteChar == '\'' || Escaped;

    public bool Consume(char character, char escapeChar, out bool preserveEscape)
    {
        preserveEscape = false;
        if (Escaped)
        {
            preserveEscape = QuoteChar == '"' && character != '$' && character != '"' && character != escapeChar;
            Escaped = false;
            return true;
        }

        if (QuoteChar == '\'')
        {
            if (character != '\'')
            {
                return true;
            }
            QuoteChar = null;
        }
        else if (character == escapeChar)
        {
            Escaped = true;
        }
        else if (character == QuoteChar)
        {
            QuoteChar = null;
        }
        else if (!QuoteChar.HasValue && character is '\'' or '"')
        {
            QuoteChar = character;
        }
        else
        {
            return true;
        }

        return false;
    }
}
