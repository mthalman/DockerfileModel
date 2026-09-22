namespace Valleysoft.DockerfileModel.Parsing;

internal static class NativeParsers
{
    internal static readonly TextParser<char> LineTerminator =
        Character.Matching(ch => ch is '\r' or '\n', "line terminator");

    internal static readonly TextParser<string> LineEnd =
        Character.EqualTo('\r')
            .Then(_ => Character.EqualTo('\n'))
            .Value("\r\n")
            .Try()
            .Try().Or(Character.EqualTo('\n').Value("\n"));

    internal static TextParser<string> Identifier(TextParser<char> first, TextParser<char> rest) =>
        Span.MatchedBy(
                from leading in first
                from trailing in rest.Try().Many()
                select leading)
            .Text();
}
