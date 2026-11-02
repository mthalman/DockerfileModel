using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class DecodedVariableParsers
{
    internal static TextParser<VariableRefToken> Variable(char escapeChar, TextParser<char> character,
        string text, Action? onBlocked = null, int start = 0, int[]? literalEnds = null, char? initialQuote = null,
        Dictionary<int, (int End, List<AggregateToken> Tokens)>? customTokens = null,
        Dictionary<int, List<AggregateToken>>? emptyReferences = null,
        Dictionary<int, LiteralToken>? customModifiers = null)
    {
        Dictionary<int, (Result<VariableRefToken> Result, bool Blocked)> parsed = new();
        bool blocked = false;
        int allowedEmptyReference = -1;
        TextParser<char> sourceCharacter = character;
        character = input =>
        {
            if (!input.IsAtEnd && literalEnds?[input.Position.Absolute] > input.Position.Absolute)
            {
                return Result.Empty<char>(input, "variable-aware source character");
            }
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
        TextParser<IEnumerable<Token>> OpaqueLiteral(bool preserveReferences) => input =>
        {
            int index = input.Position.Absolute;
            int end = !input.IsAtEnd ? literalEnds?[index] ?? index : index;
            return end > index
                ? Result.Value<IEnumerable<Token>>(
                    preserveReferences && customTokens is not null && customTokens.TryGetValue(index, out var custom)
                        ? custom.Tokens : new Token[] { new StringToken(text.Substring(index, end - index)) },
                    input, input.Skip(end - index))
                : Result.Empty<IEnumerable<Token>>(input, "non-variable literal");
        };
        TextParser<IEnumerable<Token>> opaqueLiteral = OpaqueLiteral(preserveReferences: true);

        TextParser<IEnumerable<Token>> Sequence(TextParser<IEnumerable<Token>> part)
        {
            TextParser<IEnumerable<Token>> attempt = part.Try();
            return input =>
            {
                List<Token> tokens = new();
                TextSpan remaining = input;
                while (true)
                {
                    int index = remaining.Position.Absolute;
                    if (emptyReferences is not null && emptyReferences.TryGetValue(index, out var references))
                    {
                        tokens.AddRange(references);
                    }
                    int previous = allowedEmptyReference;
                    allowedEmptyReference = index;
                    Result<IEnumerable<Token>> result;
                    try
                    {
                        result = attempt(remaining);
                    }
                    finally
                    {
                        allowedEmptyReference = previous;
                    }
                    if (!result.HasValue)
                    {
                        return Result.Value<IEnumerable<Token>>(tokens, input, remaining);
                    }
                    if (result.Remainder.Position.Absolute <= index)
                    {
                        throw new InvalidOperationException("A modifier part must consume source text.");
                    }
                    tokens.AddRange(result.Value);
                    remaining = result.Remainder;
                }
            };
        }

        TextParser<IEnumerable<Token>> Escaped() =>
            from escape in Symbol(escapeChar)
            from value in OpaqueLiteral(preserveReferences: false)
                .Select(tokens => string.Concat(tokens.Select(token => token.ToString())))
                .Or(character.Select(ch => ch.ToString()))
            select (IEnumerable<Token>)new Token[] { new StringToken(escape + value) };

        TextParser<IEnumerable<Token>> Plain(char? quote)
        {
            TextParser<char> literal = character.Where(ch => ch != escapeChar &&
                (quote.HasValue ? ch != quote : ch is not '}' and not '\'' and not '"'),
                "modifier value character");
            TextParser<char> plain = input =>
            {
                int index = input.Position.Absolute;
                if (index != allowedEmptyReference && emptyReferences?.ContainsKey(index) == true ||
                    !input.IsAtEnd && VariableParsers.IsVariableReferenceStart(text, index))
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
            opaqueLiteral.Or(nested.Cast<VariableRefToken, Token>().AsEnumerable().Try())
                .Or(Escaped().Try())
                .Or(Plain(quote));

        TextParser<IEnumerable<Token>> singleQuoted =
            from open in Symbol('\'')
            from tokens in OpaqueLiteral(preserveReferences: false).Or(
                from quotedText in character.Where(ch => ch != '\'', "single-quoted character").AtLeastOnce().Text()
                select (IEnumerable<Token>)new Token[] { new StringToken(quotedText) }).Many().Flatten()
            from close in Symbol('\'')
            select ConcatTokens(new Token[] { new StringToken("'") }, tokens,
                new Token[] { new StringToken("'") });
        TextParser<IEnumerable<Token>> doubleQuoted =
            from open in Symbol('"')
            from tokens in Sequence(Parts('"'))
            from close in Symbol('"')
            select ConcatTokens(new Token[] { new StringToken("\"") }, tokens,
                new Token[] { new StringToken("\"") });
        TextParser<IEnumerable<Token>> value =
            opaqueLiteral.Or(singleQuoted.Try()).Or(doubleQuoted.Try()).Or(Parts(null));

        TextParser<IEnumerable<Token>> modifierTokens = Sequence(value);
        TextParser<LiteralToken> modifierValue = input =>
        {
            Result<IEnumerable<Token>> result = modifierTokens(input);
            if (!result.HasValue)
            {
                return Result.CastEmpty<IEnumerable<Token>, LiteralToken>(result);
            }
            Token[] tokens = TokenHelper.CollapseStringTokens(result.Value).ToArray();
            // Wrapping a whole custom modifier would turn its nullable result into an empty string.
            LiteralToken value = tokens.Length == 1 && customModifiers is not null &&
                customModifiers.TryGetValue(input.Position.Absolute, out LiteralToken? custom) &&
                ReferenceEquals(tokens[0], custom)
                    ? custom : new LiteralToken(tokens, canContainVariables: true, escapeChar);
            return Result.Value(value, input, result.Remainder);
        };
        TextParser<IEnumerable<Token>> modifier =
            from operation in VariableRefToken.ValidModifiers
                .Select(text => text.Select(Symbol)
                    .Aggregate((first, next) => first.IgnoreThen(next)).Select(_ => text))
                .Aggregate((first, next) => first.Try().Or(next))
            from literal in modifierValue
            select ConcatTokens(operation.Select(ch => new SymbolToken(ch)),
                new Token[] { literal });
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
        List<(TextSpan Input, bool Active)> candidates = new();
        TextSpan remaining = new TextSpan(text).Skip(start);
        char? quote = initialQuote;
        Stack<char?> quotes = new();
        bool escaped = false;
        bool hasProtectedSource = false;
        while (!remaining.IsAtEnd)
        {
            int index = remaining.Position.Absolute;
            if (literalEnds?[index] > index)
            {
                remaining = remaining.Skip(literalEnds[index] - index);
                escaped = false;
                continue;
            }
            if (!sourceCharacter(remaining).HasValue)
            {
                hasProtectedSource = true;
                remaining = remaining.Skip(1);
                continue;
            }
            char current = text[index];
            if (VariableParsers.IsVariableReferenceStart(text, index))
            {
                candidates.Add((remaining, quote != '\'' && !escaped));
            }
            if (quote == '\'')
            {
                if (current == '\'')
                {
                    quote = null;
                }
            }
            else if (escaped)
            {
                escaped = false;
            }
            else if (current == escapeChar)
            {
                escaped = true;
            }
            else if (current == '"' || current == '\'' && quote is null)
            {
                quote = quote == current ? null : current;
            }
            else if (current == '$' && index + 1 < text.Length && text[index + 1] == '{')
            {
                quotes.Push(quote);
                quote = null;
            }
            else if (current == '}' && quote is null && quotes.Count > 0)
            {
                quote = quotes.Pop();
            }
            remaining = remaining.Skip(1);
        }
        for (int index = candidates.Count - 1; index >= 0; index--)
        {
            (TextSpan candidate, bool active) = candidates[index];
            // Protected source can invalidate quote prediction by blocking an enclosing reference.
            if (!hasProtectedSource && !active)
            {
                continue;
            }
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
