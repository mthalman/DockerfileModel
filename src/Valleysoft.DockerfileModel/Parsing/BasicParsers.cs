using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class BasicParsers
{
    /// <summary>
    /// Parses whitespace.
    /// </summary>
    /// <returns>Set of tokens representing whitespace.</returns>
    internal static Parser<IEnumerable<Token>> Whitespace() =>
        from whitespace in WhitespaceWithoutNewLine()
        from newLine in OptionalNewLine()
        select TokenSequences.ConcatTokens(whitespace, newLine);

    /// <summary>
    /// Parses the text of a comment, including leading whitespace.
    /// </summary>
    /// <returns>Set of tokens representing comment text.</returns>
    internal static Parser<IEnumerable<Token>> CommentText() =>
        from leading in Whitespace()
        from comment in CommentToken.GetParser()
        from lineEnd in OptionalNewLine().AsEnumerable()
        select TokenSequences.ConcatTokens(leading, new Token[] { new CommentToken(TokenSequences.ConcatTokens(comment, lineEnd)) });

    /// <summary>
    /// Optionally parses a line continuation surrounded by optional whitespace.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    internal static Parser<IEnumerable<Token>> OptionalWhitespaceOrLineContinuation(char escapeChar) =>
        from leading in Whitespace().Optional()
        from lineContinuation in LineContinuations(escapeChar).Optional()
        from trailing in Whitespace().Optional()
        select TokenSequences.ConcatTokens(
            leading.GetOrDefault(),
            lineContinuation.IsDefined ? lineContinuation.GetOrDefault() : Enumerable.Empty<Token>(),
            trailing.GetOrDefault());

    /// <summary>
    /// Parses a new line that is optional.
    /// </summary>
    /// <returns>The new line token if a new line exists; otherwise, null.</returns>
    internal static Parser<NewLineToken> OptionalNewLine() =>
        from lineEnd in P.Parse.LineEnd.Optional()
        select lineEnd.IsDefined ? new NewLineToken(lineEnd.Get()) : null;

    /// <summary>
    /// Parses a token and any trailing whitespace.
    /// </summary>
    /// <param name="parser">Token parser.</param>
    /// <returns>Set of parsed tokens.</returns>
    internal static Parser<IEnumerable<Token>> TokenWithTrailingWhitespace(Parser<Token> parser) =>
        from token in parser.AsEnumerable()
        from trailingWhitespace in Whitespace()
        select TokenSequences.ConcatTokens(token, trailingWhitespace);

    /// <summary>
    /// Parses a token and any trailing whitespace.
    /// </summary>
    /// <param name="createToken">Delegate to create the token.</param>
    /// <returns>Set of parsed tokens.</returns>
    internal static Parser<IEnumerable<Token>> TokenWithTrailingWhitespace(Func<string, Token> createToken) =>
        from val in P.Parse.AnyChar.Except(P.Parse.LineEnd).Many().Text()
        select TokenSequences.ConcatTokens(createToken(val.Trim()), GetTrailingWhitespaceToken(val)!);

    /// <summary>
    /// Returns a whitespace token for any trailing whitespace in the given string.
    /// </summary>
    /// <param name="text">String to parse.</param>
    internal static WhitespaceToken? GetTrailingWhitespaceToken(string text)
    {
        string? whitespace = new(
            text
                .Reverse()
                .TakeWhile(ch => Char.IsWhiteSpace(ch))
                .Reverse()
                .ToArray());

        if (whitespace == String.Empty)
        {
            return null;
        }

        return new WhitespaceToken(whitespace);
    }

    /// <summary>
    /// Returns a whitespace token for any leading whitespace in the given string.
    /// </summary>
    /// <param name="text">String to parse.</param>
    internal static WhitespaceToken? GetLeadingWhitespaceToken(string text)
    {
        string? whitespace = new(
            text
                .TakeWhile(ch => Char.IsWhiteSpace(ch))
                .ToArray());

        if (whitespace == String.Empty)
        {
            return null;
        }

        return new WhitespaceToken(whitespace);
    }

    /// <summary>
    /// Parses a single character preceded by an optional line continuation.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="charParser">Character parser.</param>
    /// <param name="createToken">Delegate to create the token containing the character.</param>
    /// <returns>Parsed tokens.</returns>
    internal static Parser<IEnumerable<Token>> CharWithOptionalLineContinuation(char escapeChar, Parser<char> charParser,
        Func<char, Token> createToken) =>
        from lineContinuation in LineContinuations(escapeChar)
        from ch in charParser
        select TokenSequences.ConcatTokens(lineContinuation, new Token[] { createToken(ch) });

    /// <summary>
    /// Parses a symbol.
    /// </summary>
    /// <param name="value">Symbol value.</param>
    /// <returns>A symbol token.</returns>
    internal static Parser<SymbolToken> Symbol(char value) =>
        from val in P.Parse.Char(value)
        select new SymbolToken(val);

    /// <summary>
    /// Concatenates a set of string parsers with an 'or' operator.
    /// </summary>
    /// <param name="parsers">Set of string parsers to concatenate.</param>
    /// <returns>String parser that matches on any of the given parsers.</returns>
    internal static Parser<string> OrConcat(params Parser<string>[] parsers) =>
        from vals in parsers.Aggregate((current, next) => current.Or(next)).Many()
        select String.Concat(vals);

    /// <summary>
    /// Parses a required new line.
    /// </summary>
    internal static Parser<NewLineToken> NewLine() =>
        from lineEnd in P.Parse.LineEnd
        select new NewLineToken(lineEnd);

    /// <summary>
    /// Parses any character except for whitespace.
    /// </summary>
    internal static Parser<char> NonWhitespace() =>
        P.Parse.AnyChar.Except(P.Parse.WhiteSpace);

    /// <summary>
    /// Parses multiple line continuations and any whitespace.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    internal static Parser<IEnumerable<Token>> LineContinuations(char escapeChar) =>
        LineContinuationToken.GetParser(escapeChar).Many();

    /// <summary>
    /// Parses all whitespace except a new line.
    /// </summary>
    internal static Parser<WhitespaceToken?> WhitespaceWithoutNewLine() =>
        from whitespace in P.Parse.WhiteSpace.Except(P.Parse.LineTerminator).XMany().Text()
        select whitespace.Length > 0 ? new WhitespaceToken(whitespace) : null;

    /// <summary>
    /// Concatenates a set of token parsers with an 'or' operator.
    /// </summary>
    /// <param name="parsers">Set of string parsers to concatenate.</param>
    /// <returns>String parser that matches on any of the given parsers.</returns>
    internal static Parser<IEnumerable<Token>> OrConcat(params Parser<IEnumerable<Token>>[] parsers) =>
        from vals in (parsers.Aggregate((current, next) => current.Or(next))).Many()
        select vals.SelectMany(val => val);

    /// <summary>
    /// Transforms a character parser into a parser for a set of tokens containing a single string token.
    /// </summary>
    /// <param name="parser">Character parser.</param>
    internal static Parser<IEnumerable<Token>> ToStringTokens(Parser<char> parser) =>
        from ch in parser
        select new Token[] { new StringToken(ch.ToString()) };
}
