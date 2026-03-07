using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class LabelKeyToken : IdentifierToken
{
    private readonly char escapeChar;

    public LabelKeyToken(string value, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(value, GetInnerParser(escapeChar)), escapeChar)
    {
    }

    private LabelKeyToken((IEnumerable<Token> Tokens, char? QuoteChar) tokensInfo, char escapeChar)
        : this(tokensInfo.Tokens, escapeChar)
    {
        QuoteChar = tokensInfo.QuoteChar;
    }

    internal LabelKeyToken(IEnumerable<Token> tokens, char escapeChar)
        : base(tokens)
    {
        this.escapeChar = escapeChar;
    }

    public static Parser<LabelKeyToken> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from result in GetInnerParser(escapeChar)
        select new LabelKeyToken(result.Tokens, escapeChar)
        {
            QuoteChar = result.QuoteChar
        };

    protected override IEnumerable<Token> GetInnerTokens(string value) =>
        GetTokens(value, GetInnerParser(escapeChar)).Tokens;

    private static Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> GetInnerParser(char escapeChar) =>
        IdentifierTokens(FirstCharParser(), TailCharParser(), escapeChar);

    private static Parser<char> FirstCharParser() =>
        Sprache.Parse.Letter
            .Or(Sprache.Parse.Char('_'))
            .Or(Sprache.Parse.Char('.'));

    private static Parser<char> TailCharParser() =>
        Sprache.Parse.LetterOrDigit
            .Or(Sprache.Parse.Char('_'))
            .Or(Sprache.Parse.Char('-'))
            .Or(Sprache.Parse.Char('.'));
}
