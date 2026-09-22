
namespace Valleysoft.DockerfileModel.Tokens;

public class LineContinuationToken : AggregateToken
{
    public LineContinuationToken(char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(Environment.NewLine, escapeChar)
    {
    }

    public LineContinuationToken(string newLine, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens($"{escapeChar}{newLine}", GetInnerParser(escapeChar)))
    {
    }

    internal LineContinuationToken(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static LineContinuationToken Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    /// <summary>
    /// Parses a line continuation, consisting of an escape character followed by a new line.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <returns>Line continuation tokens.</returns>
    internal static TextParser<LineContinuationToken> GetParser(char escapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new LineContinuationToken(tokens);

    private static TextParser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        from escape in Symbol(escapeChar)
        from whitespace in Character.Matching(
            ch => char.IsWhiteSpace(ch) && ch is not ('\r' or '\n'),
            "horizontal whitespace").Try().Many()
        from lineEnding in NativeParsers.LineEnd
        select ConcatTokens(
            escape,
            whitespace.Any() ? new WhitespaceToken(new string(whitespace.ToArray())) : null,
            new NewLineToken(lineEnding));
}
