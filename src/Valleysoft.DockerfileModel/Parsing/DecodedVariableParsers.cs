using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class DecodedVariableParsers
{
    internal static TextParser<VariableRefToken> Variable(char escapeChar, TextParser<char> character)
    {
        TextParser<char> Symbol(char value) => character.Where(ch => ch == value, $"`{value}`");
        TextParser<string> name = character.Where(ch => char.IsLetterOrDigit(ch) || ch == '_',
            "variable name").AtLeastOnce().Text();
        TextParser<VariableRefToken>? parser = null;
        TextParser<VariableRefToken> nested = input =>
            (parser ?? throw new InvalidOperationException("The reference parser is not initialized."))(input);

        TextParser<IEnumerable<Token>> Escaped() =>
            from escape in Symbol(escapeChar)
            from value in character
            select (IEnumerable<Token>)new Token[] { new StringToken(new string(new[] { escape, value })) };

        TextParser<IEnumerable<Token>> Plain(char? quote)
        {
            TextParser<char> variableStart = Character.EqualTo('$').IgnoreThen(Character.Matching(
                ch => char.IsLetterOrDigit(ch) || ch is '_' or '{', "variable start"));
            TextParser<char> literal = character.Where(ch => ch != escapeChar &&
                (quote.HasValue ? ch != quote : ch is not '}' and not '\'' and not '"'),
                "modifier value character");
            TextParser<char> plain = input =>
            {
                if (variableStart(input).HasValue)
                {
                    return Result.Empty<char>(input, "literal character");
                }
                return literal(input);
            };
            return
                from values in plain.AtLeastOnce().Text()
                select (IEnumerable<Token>)new Token[] { new StringToken(values) };
        }

        TextParser<IEnumerable<Token>> Parts(char? quote) =>
            nested.Cast<VariableRefToken, Token>().AsEnumerable().Try()
                .Or(Escaped().Try())
                .Or(Plain(quote));

        TextParser<IEnumerable<Token>> singleQuoted =
            from open in Symbol('\'')
            from text in character.Where(ch => ch != '\'', "single-quoted character").Many().Text()
            from close in Symbol('\'')
            select (IEnumerable<Token>)new Token[] { new StringToken($"'{text}'") };
        TextParser<IEnumerable<Token>> doubleQuoted =
            from open in Symbol('"')
            from tokens in Parts('"').Try().Many().Flatten()
            from close in Symbol('"')
            select ConcatTokens(new Token[] { new StringToken("\"") }, tokens,
                new Token[] { new StringToken("\"") });
        TextParser<IEnumerable<Token>> value =
            singleQuoted.Try().Or(doubleQuoted.Try()).Or(Parts(null));

        TextParser<IEnumerable<Token>> modifier =
            from operation in VariableRefToken.ValidModifiers
                .Select(text => Span.EqualTo(text).Text())
                .Aggregate((first, next) => first.Try().Or(next))
            from tokens in value.Try().Many().Flatten()
            select ConcatTokens(operation.Select(ch => new SymbolToken(ch)),
                new Token[] { new LiteralToken(TokenHelper.CollapseStringTokens(tokens),
                    canContainVariables: true, escapeChar) });
        TextParser<IEnumerable<Token>> braced =
            from open in Symbol('{')
            from identifier in name
            from operation in modifier.Try().OptionalOrDefault(Enumerable.Empty<Token>())
            from close in Symbol('}')
            select ConcatTokens(new Token[] { new SymbolToken('{'), new StringToken(identifier) },
                operation, new Token[] { new SymbolToken('}') });
        parser =
            from dollar in Symbol('$')
            from tokens in braced.Try().Or(name.Select(identifier =>
                (IEnumerable<Token>)new Token[] { new StringToken(identifier) }))
            select new VariableRefToken(tokens, escapeChar);
        return parser;
    }
}
