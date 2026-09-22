using Valleysoft.DockerfileModel.Tokens;


namespace Valleysoft.DockerfileModel;

public class StageName : IdentifierToken
{
    private readonly char escapeChar;

    internal StageName(IEnumerable<Token> tokens, char escapeChar) : base(tokens)
    {
        this.escapeChar = escapeChar;
        EditingEscapeChar = escapeChar;
    }

    public StageName(string value, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(value, GetInnerParser(escapeChar)), escapeChar)
    {
    }

    internal static Parser<StageName> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new StageName(tokens, escapeChar);

    protected override IEnumerable<Token> GetInnerTokens(string value) =>
        GetTokens(value, GetInnerParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        IdentifierString(escapeChar, FirstCharParser(), TailCharParser());

    private static Parser<char> FirstCharParser() => P.Parse.Letter;

    private static Parser<char> TailCharParser() =>
        P.Parse.LetterOrDigit
            .Or(P.Parse.Char('_'))
            .Or(P.Parse.Char('-'))
            .Or(P.Parse.Char('.'));
}
