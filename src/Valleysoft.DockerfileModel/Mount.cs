using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Represents a mount specification for RUN --mount flags.
/// Handles all BuildKit mount types (bind, cache, tmpfs, secret, ssh) by parsing
/// the mount value as a type=X prefix followed by zero or more comma-separated key=value pairs.
/// </summary>
public class Mount : AggregateToken
{
    internal Mount(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public string Type
    {
        get => TypeToken.Value;
        set
        {
            Requires.NotNullOrEmpty(value, nameof(value));
            var valueToken = TypeToken.ValueToken
                ?? throw new InvalidOperationException("Mount.TypeToken.ValueToken cannot be null when setting Mount.Type.");
            valueToken.Value = value;
        }
    }

    public KeyValueToken<KeywordToken, LiteralToken> TypeToken
    {
        get => Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>().First();
        set
        {
            Requires.NotNull(value, nameof(value));
            SetToken(TypeToken, value);
        }
    }

    public static Mount Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<Mount> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new Mount(tokens);

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar)
    {
        Parser<LiteralToken> valueParser = LiteralWithVariables(
            escapeChar, new char[] { ',' });

        Parser<KeyValueToken<KeywordToken, LiteralToken>> keyValueParser =
            KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                KeywordToken.GetParser(escapeChar), valueParser, escapeChar: escapeChar);

        // Each comma-separated entry is either a key=value pair or a bare keyword (e.g. "required", "readonly")
        Parser<Token> entryParser =
            keyValueParser.Cast<KeyValueToken<KeywordToken, LiteralToken>, Token>()
            .Or(KeywordToken.GetParser(escapeChar).Cast<KeywordToken, Token>());

        // Parse: type=X followed by zero or more comma-separated entries
        // Line continuations can appear between comma-separated pairs
        return
            from type in ArgTokens(
                KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                    KeywordToken.GetParser("type", escapeChar), valueParser, escapeChar: escapeChar).AsEnumerable(), escapeChar)
            from rest in (
                from lineCont1 in LineContinuations(escapeChar)
                from comma in Symbol(',')
                from lineCont2 in LineContinuations(escapeChar)
                from entry in entryParser
                select ConcatTokens(lineCont1, new Token[] { comma }, lineCont2, new Token[] { entry })).Many()
            select ConcatTokens(type, rest.SelectMany(t => t));
    }
}
