using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class DecodedVariableParsers
{
    internal static TextParser<VariableRefToken> Variable(char escapeChar, TextParser<char> character,
        string text, Action? onBlocked = null, int start = 0, char? initialQuote = null)
    {
        Dictionary<int, (Result<VariableRefToken> Result, bool Blocked)> parsed = new();
        bool blocked = false;
        TextParser<char> sourceCharacter = character;
        character = input =>
        {
            Result<char> result = sourceCharacter(input);
            blocked |= !input.IsAtEnd && !result.HasValue;
            return result;
        };
        TextParser<char> Symbol(char value) => character.Where(ch => ch == value, $"`{value}`");
        TextParser<string> name = character.Where(ch => char.IsLetterOrDigit(ch) || ch == '_',
            "variable name").AtLeastOnce().Text();
        Result<VariableRefToken> CachedReference(TextSpan input)
        {
            if (parsed.TryGetValue(input.Position.Absolute, out var cached))
            {
                blocked |= cached.Blocked;
                return cached.Result;
            }
            return Result.Empty<VariableRefToken>(input, "variable reference");
        }
        TextParser<VariableRefToken> nested = CachedReference;

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
            from quotedText in character.Where(ch => ch != '\'', "single-quoted character").Many().Text()
            from close in Symbol('\'')
            select (IEnumerable<Token>)new Token[] { new StringToken($"'{quotedText}'") };
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
                .Select(text => text.Select(Symbol)
                    .Aggregate((first, next) => first.IgnoreThen(next)).Select(_ => text))
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
        TextParser<VariableRefToken> parser =
            from dollar in Symbol('$')
            from tokens in braced.Try().Or(name.Select(identifier =>
                (IEnumerable<Token>)new Token[] { new StringToken(identifier) }))
            select new VariableRefToken(tokens, escapeChar);
        List<TextSpan> candidates = new();
        TextSpan remaining = new TextSpan(text).Skip(start);
        char? quote = initialQuote;
        Stack<char?> quotes = new();
        while (!remaining.IsAtEnd)
        {
            int index = remaining.Position.Absolute;
            char current = text[index];
            if (!sourceCharacter(remaining).HasValue)
            {
                remaining = remaining.Skip(1);
                continue;
            }
            if (quote == '\'')
            {
                if (current == '\'')
                {
                    quote = null;
                }
            }
            else if (current == escapeChar && index + 1 < text.Length)
            {
                remaining = remaining.Skip(2);
                continue;
            }
            else if (current == '"' || current == '\'' && quote is null)
            {
                quote = quote == current ? null : current;
            }
            else if (current == '$')
            {
                candidates.Add(remaining);
                if (index + 1 < text.Length && text[index + 1] == '{')
                {
                    quotes.Push(quote);
                    quote = null;
                }
            }
            else if (current == '}' && quote is null && quotes.Count > 0)
            {
                quote = quotes.Pop();
            }
            remaining = remaining.Skip(1);
        }
        for (int index = candidates.Count - 1; index >= 0; index--)
        {
            TextSpan candidate = candidates[index];
            blocked = false;
            Result<VariableRefToken> result = parser(candidate);
            parsed.Add(candidate.Position.Absolute, (result, blocked));
        }
        return input =>
        {
            if (!ReferenceEquals(input.Source, text))
            {
                throw new InvalidOperationException("The reference parser belongs to a different decoded path.");
            }
            blocked = false;
            Result<VariableRefToken> result = CachedReference(input);
            if (blocked)
            {
                onBlocked?.Invoke();
            }
            return result;
        };
    }
}
