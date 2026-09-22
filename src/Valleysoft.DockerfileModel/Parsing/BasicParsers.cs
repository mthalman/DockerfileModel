using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class BasicParsers
{
    /// <summary>
    /// Parses whitespace.
    /// </summary>
    /// <returns>Set of tokens representing whitespace.</returns>
    internal static TextParser<IEnumerable<Token>> Whitespace() =>
        from whitespace in WhitespaceWithoutNewLine()
        from newLine in OptionalNewLine()
        select TokenSequences.ConcatTokens(whitespace, newLine);

    /// <summary>
    /// Parses the text of a comment, including leading whitespace.
    /// </summary>
    /// <returns>Set of tokens representing comment text.</returns>
    internal static TextParser<IEnumerable<Token>> CommentText() =>
        from leading in Whitespace()
        from comment in CommentToken.GetParser()
        from lineEnd in OptionalNewLine().AsEnumerable()
        select TokenSequences.ConcatTokens(leading, new Token[] { new CommentToken(TokenSequences.ConcatTokens(comment, lineEnd)) });

    /// <summary>
    /// Optionally parses a line continuation surrounded by optional whitespace.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    internal static TextParser<IEnumerable<Token>> OptionalWhitespaceOrLineContinuation(char escapeChar) =>
        from leading in Whitespace().Try().OptionalOrDefault(Enumerable.Empty<Token>())
        from lineContinuation in LineContinuations(escapeChar).Try().OptionalOrDefault(Enumerable.Empty<Token>())
        from trailing in Whitespace().Try().OptionalOrDefault(Enumerable.Empty<Token>())
        select TokenSequences.ConcatTokens(
            leading,
            lineContinuation,
            trailing);

    /// <summary>
    /// Parses a new line that is optional.
    /// </summary>
    /// <returns>The new line token if a new line exists; otherwise, null.</returns>
    internal static TextParser<NewLineToken> OptionalNewLine() =>
        from lineEnd in NativeParsers.LineEnd.OptionalOrDefault(null!)
        select lineEnd is null ? null : new NewLineToken(lineEnd);

    /// <summary>
    /// Parses a token and any trailing whitespace.
    /// </summary>
    /// <param name="parser">Token parser.</param>
    /// <returns>Set of parsed tokens.</returns>
    internal static TextParser<IEnumerable<Token>> TokenWithTrailingWhitespace<TToken>(TextParser<TToken> parser)
        where TToken : Token =>
        from token in parser.AsEnumerable()
        from trailingWhitespace in Whitespace()
        select TokenSequences.ConcatTokens(token.Cast<Token>(), trailingWhitespace);

    /// <summary>
    /// Parses a token and any trailing whitespace.
    /// </summary>
    /// <param name="createToken">Delegate to create the token.</param>
    /// <returns>Set of parsed tokens.</returns>
    internal static TextParser<IEnumerable<Token>> TokenWithTrailingWhitespace(Func<string, Token> createToken) =>
        from val in Superpower.Parse.Not(NativeParsers.LineEnd)
            .IgnoreThen(Character.AnyChar)
            .Try().Many()
            .Text()
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
    internal static TextParser<IEnumerable<Token>> CharWithOptionalLineContinuation(char escapeChar, TextParser<char> charParser,
        Func<char, Token> createToken) =>
        from lineContinuation in LineContinuations(escapeChar)
        from ch in charParser
        select TokenSequences.ConcatTokens(lineContinuation, new Token[] { createToken(ch) });

    /// <summary>
    /// Parses a symbol.
    /// </summary>
    /// <param name="value">Symbol value.</param>
    /// <returns>A symbol token.</returns>
    internal static TextParser<SymbolToken> Symbol(char value) =>
        from val in Character.EqualTo(value)
        select new SymbolToken(val);

    /// <summary>
    /// Concatenates a set of string parsers with an 'or' operator.
    /// </summary>
    /// <param name="parsers">Set of string parsers to concatenate.</param>
    /// <returns>String parser that matches on any of the given parsers.</returns>
    internal static TextParser<string> OrConcat(params TextParser<string>[] parsers) =>
        from vals in parsers.Aggregate((current, next) => current.Try().Or(next)).Try().AtLeastOnce()
        select String.Concat(vals);

    /// <summary>
    /// Parses a required new line.
    /// </summary>
    internal static TextParser<NewLineToken> NewLine() =>
        from lineEnd in NativeParsers.LineEnd
        select new NewLineToken(lineEnd);

    /// <summary>
    /// Parses any character except for whitespace.
    /// </summary>
    internal static TextParser<char> NonWhitespace() =>
        Character.Matching(ch => !char.IsWhiteSpace(ch), "non-whitespace");

    /// <summary>
    /// Parses multiple line continuations and any whitespace.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    internal static TextParser<IEnumerable<Token>> LineContinuations(char escapeChar) =>
        LineContinuationToken.GetParser(escapeChar)
            .Try().Many()
            .Select(tokens => tokens.Cast<Token>());

    /// <summary>
    /// Parses all whitespace except a new line.
    /// </summary>
    internal static TextParser<WhitespaceToken?> WhitespaceWithoutNewLine() =>
        from whitespace in Character.Matching(
            ch => char.IsWhiteSpace(ch) && ch is not ('\r' or '\n'),
            "horizontal whitespace").Try().Many().Text()
        select whitespace.Length > 0 ? new WhitespaceToken(whitespace) : null;

    /// <summary>
    /// Concatenates a set of token parsers with an 'or' operator.
    /// </summary>
    /// <param name="parsers">Set of string parsers to concatenate.</param>
    /// <returns>String parser that matches on any of the given parsers.</returns>
    internal static TextParser<IEnumerable<Token>> OrConcat(params TextParser<IEnumerable<Token>>[] parsers) =>
        from vals in parsers.Aggregate((current, next) => current.Try().Or(next)).Try().AtLeastOnce()
        select vals.SelectMany(val => val);

    /// <summary>
    /// Transforms a character parser into a parser for a set of tokens containing a single string token.
    /// </summary>
    /// <param name="parser">Character parser.</param>
    internal static TextParser<IEnumerable<Token>> ToStringTokens(TextParser<char> parser) =>
        from ch in parser
        select (IEnumerable<Token>)new Token[] { new StringToken(ch.ToString()) };
}
