using System.Text;

using static Valleysoft.DockerfileModel.Parsing.StringParsers;

namespace Valleysoft.DockerfileModel.Tokens;

public class KeywordToken : AggregateToken, IValueToken
{
    public KeywordToken(string value, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(GetTokens(value, GetInnerParser(StripLineContinuations(value, escapeChar), escapeChar)))
    {
        EditingEscapeChar = escapeChar;
    }

    internal KeywordToken(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar) : base(tokens)
    {
        EditingEscapeChar = escapeChar;
    }

    public string Value => this.ToString(TokenStringOptions.CreateOptionsForValueString());

    string IValueToken.Value
    {
        get => Value;
        set => throw new NotSupportedException("The value of a keyword is read-only.");
    }

    [Obsolete("Use Dockerfile.Parse or Dockerfile.TryParse instead. Parser factories will be removed in the next major version.")]
    public static Parser<KeywordToken> GetParser(string keyword, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(keyword, escapeChar)
        select new KeywordToken(tokens, escapeChar);

    /// <summary>
    /// Parses any keyword: starts with a letter, digit, underscore, or hyphen, followed by zero or
    /// more letters, digits, underscores, hyphens, line continuations, or escaped characters.
    /// Used for generic key-value parsing where the key name is not known in advance.
    /// </summary>
    [Obsolete("Use Dockerfile.Parse or Dockerfile.TryParse instead. Parser factories will be removed in the next major version.")]
    public static Parser<KeywordToken> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in IdentifierString(
            escapeChar,
            Parse.LetterOrDigit.Or(Parse.Char('_')).Or(Parse.Char('-')),
            Parse.LetterOrDigit.Or(Parse.Char('_')).Or(Parse.Char('-')))
        select new KeywordToken(tokens, escapeChar);

    private static string StripLineContinuations(string value, char escapeChar)
    {
        StringBuilder builder = new();
        foreach (char ch in value)
        {
            if (ch == escapeChar || Char.IsWhiteSpace(ch))
            {
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(string value, char escapeChar) =>
        StringToken(value, escapeChar);
}
