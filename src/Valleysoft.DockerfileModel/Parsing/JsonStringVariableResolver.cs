using System.Text;
using System.Text.Json;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class JsonStringVariableResolver
{
    internal static LiteralToken Tokenize(LiteralToken path, char escapeChar)
    {
        string physical = path.ToString();
        (Token Token, string Text)[] source = path.Tokens.Select(token => (token, token.ToString())).ToArray();
        List<(int Start, int End, Token Token, bool Inline)> references = new();
        HashSet<int> completeBraces = FindCompleteBracedReferences(physical, escapeChar);
        Dictionary<int, Result<VariableRefToken>> parsed = new();
        Result<VariableRefToken> ParseReference(TextSpan span) =>
            parsed.TryGetValue(span.Position.Absolute, out Result<VariableRefToken> result)
                ? result
                : Result.Empty<VariableRefToken>(span, "variable reference");
        TextParser<VariableRefToken> parser = VariableRefToken.GetParser(escapeChar, ParseReference);
        List<TextSpan> candidates = new();
        TextSpan input = new TextSpan(physical).Skip(1);
        while (input.Position.Absolute < physical.Length - 1)
        {
            int start = input.Position.Absolute;
            if (physical[start] == escapeChar && start + 1 < physical.Length - 1)
            {
                input = input.Skip(2);
                continue;
            }
            if (physical[start] == '$' &&
                (physical[start + 1] != '{' || completeBraces.Contains(start)))
            {
                candidates.Add(input);
            }
            input = input.Skip(1);
        }
        // Bottom-up parsing avoids CLR recursion even for balanced, deeply nested modifiers.
        for (int index = candidates.Count - 1; index >= 0; index--)
        {
            TextSpan candidate = candidates[index];
            parsed.Add(candidate.Position.Absolute, parser(candidate));
        }
        input = new TextSpan(physical).Skip(1);
        while (input.Position.Absolute < physical.Length - 1)
        {
            int start = input.Position.Absolute;
            if (physical[start] == escapeChar && start + 1 < physical.Length - 1)
            {
                input = input.Skip(2);
                continue;
            }
            if (physical[start] == '$')
            {
                if (start + 1 < physical.Length && physical[start + 1] == '{' && !completeBraces.Contains(start))
                {
                    input = input.Skip(1);
                    continue;
                }
                Result<VariableRefToken> reference = ParseReference(input);
                if (reference.HasValue && reference.Remainder.Position.Absolute < physical.Length)
                {
                    references.Add((start, reference.Remainder.Position.Absolute, reference.Value, false));
                    input = reference.Remainder;
                    continue;
                }
            }
            input = input.Skip(1);
        }
        // Encoded JSON may protect syntactically incomplete references; semantic validation happens after decoding.
        return new LiteralToken(references.Count == 0 ? path.Tokens :
            BuildTokens(source, references, physical.Length - 1), canContainVariables: true, escapeChar)
        {
            QuoteChar = path.QuoteChar
        };
    }

    private static HashSet<int> FindCompleteBracedReferences(string physical, char escapeChar)
    {
        List<int> offsets = new();
        string logical = ConstructReader.FoldHeader(physical, 0, escapeChar, out _, offsets);
        Stack<int> openings = new();
        HashSet<int> complete = new();
        for (int index = 1; index < logical.Length - 1; index++)
        {
            if (logical[index] == escapeChar)
            {
                index++;
            }
            else if (logical[index] == '$' && index + 1 < logical.Length && logical[index + 1] == '{' &&
                offsets[index + 1] == offsets[index] + 1)
            {
                openings.Push(offsets[index]);
                index++;
            }
            else if (logical[index] == '}' && openings.Count > 0)
            {
                complete.Add(openings.Pop());
            }
        }
        return complete;
    }

    internal static string Resolve(LiteralToken path, string physical, string logical, List<int> offsets, char escapeChar,
        IDictionary<string, string?> variables, ResolutionOptions options, bool expandVariables)
    {
        string decoded = Decode(logical, offsets, out List<int> starts, out List<int> ends);
        Position PhysicalPosition(int decodedOffset) => new TextSpan(physical).Skip(
            decodedOffset < starts.Count ? starts[decodedOffset] : offsets[logical.Length - 1]).Position;
        (Token Token, string Text)[] source = path.Tokens.Select(token => (token, token.ToString())).ToArray();
        int[] resolvedCounts = new int[decoded.Length + 1];
        int sourceIndex = 0;
        int sourceStart = 1;
        for (int index = 0; index < decoded.Length; index++)
        {
            while (sourceIndex < source.Length && sourceStart + source[sourceIndex].Text.Length <= starts[index])
            {
                sourceStart += source[sourceIndex].Text.Length;
                sourceIndex++;
            }
            resolvedCounts[index + 1] = resolvedCounts[index] +
                (sourceIndex < source.Length && path.IsResolvedJsonLiteral(source[sourceIndex].Token) ? 1 : 0);
        }

        List<(int Start, int End, Token Token, bool Inline)> replacements = new();
        ResolutionOptions valueOptions = new() { RemoveEscapeCharacters = options.RemoveEscapeCharacters };
        bool blocked = false;
        TextParser<char> character = input =>
        {
            int index = input.Position.Absolute;
            if (!input.IsAtEnd && resolvedCounts[index + 1] != resolvedCounts[index])
            {
                return Result.Empty<char>(input, "unresolved source character");
            }
            return Character.AnyChar(input);
        };
        TextParser<VariableRefToken> variableParser =
            DecodedVariableParsers.Variable(escapeChar, character, decoded, () => blocked = true);
        TextSpan input = new(decoded);
        int tokenIndex = 0;
        int tokenStart = 1;
        char? quote = null;
        while (!input.IsAtEnd)
        {
            int index = input.Position.Absolute;
            while (tokenIndex < source.Length && tokenStart + source[tokenIndex].Text.Length <= starts[index])
            {
                tokenStart += source[tokenIndex].Text.Length;
                tokenIndex++;
            }
            if (tokenIndex < source.Length && path.IsResolvedJsonLiteral(source[tokenIndex].Token))
            {
                int end = index + 1;
                int tokenEnd = tokenStart + source[tokenIndex].Text.Length;
                while (end < decoded.Length && starts[end] < tokenEnd)
                {
                    end++;
                }
                if (options.RemoveEscapeCharacters)
                {
                    string literal = decoded.Substring(index, end - index);
                    string formatted = options.FormatValue(escapeChar, literal);
                    if (formatted != literal)
                    {
                        replacements.Add((starts[index], ends[end - 1], new StringToken(Encode(formatted)), false));
                    }
                }
                input = input.Skip(end - index);
                continue;
            }
            char current = decoded[index];
            if (quote == '\'')
            {
                if (current == escapeChar && options.RemoveEscapeCharacters)
                {
                    int end = index + 1;
                    string value = string.Empty;
                    if (end < decoded.Length && decoded[end] == escapeChar)
                    {
                        end++;
                        value = escapeChar.ToString();
                    }
                    replacements.Add((starts[index], ends[end - 1], new StringToken(Encode(value)), false));
                    input = input.Skip(end - index);
                    continue;
                }
                if (current == '\'')
                {
                    quote = null;
                }
                input = input.Skip(1);
                continue;
            }
            if (current == escapeChar)
            {
                if (index + 1 == decoded.Length)
                {
                    if (options.RemoveEscapeCharacters)
                    {
                        replacements.Add((starts[index], ends[index], new StringToken(string.Empty), false));
                    }
                    input = input.Skip(1);
                    continue;
                }
                int length = char.IsHighSurrogate(decoded[index + 1]) &&
                    index + 2 < decoded.Length && char.IsLowSurrogate(decoded[index + 2]) ? 2 : 1;
                if (options.RemoveEscapeCharacters)
                {
                    replacements.Add((starts[index], ends[index + length],
                        new StringToken(Encode(decoded.Substring(index + 1, length))), false));
                }
                input = input.Skip(length + 1);
                continue;
            }
            if (current == '"' || (current == '\'' && quote is null))
            {
                quote = quote == current ? null : current;
            }
            if (expandVariables && current == '$')
            {
                blocked = false;
                Result<VariableRefToken> variable = variableParser(input);
                if (variable.HasValue &&
                    resolvedCounts[variable.Remainder.Position.Absolute] == resolvedCounts[index])
                {
                    int end = variable.Remainder.Position.Absolute;
                    if (!VariableRefToken.IsPatternModifier(variable.Value.Modifier))
                    {
                        string value = ResolveReference(variable.Value, escapeChar, variables, valueOptions);
                        replacements.Add((starts[index], ends[end - 1], new StringToken(Encode(value)), true));
                    }
                    input = variable.Remainder;
                    continue;
                }
                if (!blocked && index + 1 < decoded.Length && decoded[index + 1] == '{')
                {
                    throw new ParseException("Invalid variable reference in decoded JSON path.",
                        PhysicalPosition(variable.ErrorPosition.Absolute));
                }
            }
            input = input.Skip(1);
        }

        if (expandVariables && quote is not null)
        {
            throw new ParseException("Unterminated quote in decoded JSON path.",
                PhysicalPosition(input.Position.Absolute));
        }
        if (replacements.Count == 0)
        {
            return physical;
        }
        List<Token> substitutions = new();
        bool inlineOnly = replacements.All(replacement => replacement.Inline);
        List<Token> tokens = BuildTokens(source, replacements, physical.Length - 1,
            options.UpdateInline && inlineOnly ? substitutions : null);
        string result = "\"" + string.Concat(tokens.Select(token => token.ToString())) + "\"";
        if (options.UpdateInline && replacements.Any(replacement => replacement.Inline))
        {
            path.ReplaceWithTokens(inlineOnly
                ? tokens
                : BuildTokens(source, replacements.Where(replacement => replacement.Inline), physical.Length - 1,
                    substitutions));
            path.RecordResolvedJsonLiterals(substitutions);
        }
        return result;
    }

    private static string ResolveReference(VariableRefToken root, char escapeChar,
        IDictionary<string, string?> variables, ResolutionOptions options)
    {
        Dictionary<VariableRefToken, string> resolved = new(ReferenceComparer<VariableRefToken>.Instance);
        Stack<(VariableRefToken Reference, bool Ready)> pending = new();
        pending.Push((root, false));
        while (pending.Count > 0)
        {
            (VariableRefToken reference, bool ready) = pending.Pop();
            if (resolved.ContainsKey(reference))
            {
                continue;
            }
            if (VariableRefToken.IsPatternModifier(reference.Modifier))
            {
                resolved.Add(reference, RenderReference(reference));
                continue;
            }
            LiteralToken? modifier = reference.GetRequiredModifierValue(variables);
            if (modifier is null)
            {
                resolved.Add(reference, reference.ResolveVariables(escapeChar, variables, options) ?? string.Empty);
            }
            else if (!ready)
            {
                pending.Push((reference, true));
                foreach (VariableRefToken child in modifier.Tokens.OfType<VariableRefToken>())
                {
                    pending.Push((child, false));
                }
            }
            else
            {
                string value = string.Concat(modifier.Tokens.Select(token => token is VariableRefToken child
                    ? resolved[child]
                    : options.FormatValue(escapeChar, token.ToString())));
                resolved.Add(reference, reference.ResolveWithModifierValue(escapeChar, variables, options, value)
                    ?? string.Empty);
            }
        }
        return resolved[root];
    }

    private static string RenderReference(VariableRefToken reference)
    {
        StringBuilder builder = new();
        Stack<(Token Token, bool Close)> pending = new();
        pending.Push((reference, false));
        while (pending.Count > 0)
        {
            (Token token, bool close) = pending.Pop();
            if (close)
            {
                builder.Append(((IQuotableToken)token).QuoteChar);
            }
            else if (token is AggregateToken aggregate)
            {
                if (token is VariableRefToken)
                {
                    builder.Append('$');
                }
                if (token is IQuotableToken quotable && quotable.QuoteChar.HasValue)
                {
                    builder.Append(quotable.QuoteChar);
                    pending.Push((token, true));
                }
                foreach (Token child in aggregate.Tokens.Reverse())
                {
                    pending.Push((child, false));
                }
            }
            else
            {
                builder.Append(token.ToString());
            }
        }
        return builder.ToString();
    }

    private static string Decode(string logical, List<int> offsets, out List<int> starts, out List<int> ends)
    {
        List<char> decoded = new();
        // Physical spans keep untouched JSON escapes and Dockerfile trivia intact.
        List<int> characterStarts = new();
        List<int> characterEnds = new();
        void AddCharacter(char value, int index, int length)
        {
            decoded.Add(value);
            characterStarts.Add(offsets[index]);
            characterEnds.Add(offsets[index + length - 1] + 1);
        }

        for (int index = 1; index < logical.Length - 1;)
        {
            int length = logical[index] == '\\' ? logical[index + 1] == 'u' ? 6 : 2 : 1;
            char value = length == 1 ? logical[index] : logical[index + 1] switch
            {
                'u' => (char)Convert.ToInt32(logical.Substring(index + 2, 4), 16),
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => logical[index + 1]
            };
            if (char.IsHighSurrogate(value))
            {
                int next = index + length;
                int nextLength = length == 6 && next + 6 < logical.Length &&
                    logical[next] == '\\' && logical[next + 1] == 'u' ? 6 : length == 1 ? 1 : 0;
                char low = nextLength == 6
                    ? (char)Convert.ToInt32(logical.Substring(next + 2, 4), 16)
                    : next < logical.Length - 1 ? logical[next] : '\0';
                if (nextLength > 0 && char.IsLowSurrogate(low))
                {
                    AddCharacter(value, index, length);
                    AddCharacter(low, next, nextLength);
                    index = next + nextLength;
                    continue;
                }
            }
            // BuildKit uses Go's JSON decoder, which replaces unmatched surrogates rather than rejecting them.
            AddCharacter(char.IsSurrogate(value) ? '\uFFFD' : value, index, length);
            index += length;
        }
        starts = characterStarts;
        ends = characterEnds;
        return new string(decoded.ToArray());
    }

    private static string Encode(string value) => JsonEncodedText.Encode(value).ToString();

    private static List<Token> BuildTokens((Token Token, string Text)[] source,
        IEnumerable<(int Start, int End, Token Token, bool Inline)> replacements, int end,
        List<Token>? substitutions = null)
    {
        List<Token> tokens = new();
        int tokenIndex = 0;
        int tokenStart = 1;
        void CopyUnchanged(int start, int stop)
        {
            while (tokenIndex < source.Length && tokenStart < stop)
            {
                (Token token, string text) = source[tokenIndex];
                int tokenEnd = tokenStart + text.Length;
                int first = Math.Max(start - tokenStart, 0);
                int last = Math.Min(stop - tokenStart, text.Length);
                if (first < last)
                {
                    tokens.Add(first == 0 && last == text.Length
                        ? token
                        : new StringToken(text.Substring(first, last - first)));
                }
                if (tokenEnd > stop)
                {
                    break;
                }
                tokenStart = tokenEnd;
                tokenIndex++;
            }
        }

        int position = 1;
        foreach ((int start, int finish, Token substitution, bool inline) in replacements)
        {
            CopyUnchanged(position, start);
            tokens.Add(substitution);
            if (inline)
            {
                substitutions?.Add(substitution);
            }
            position = finish;
        }
        CopyUnchanged(position, end);
        return tokens;
    }
}
