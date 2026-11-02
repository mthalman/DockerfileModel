using System.Text;
using System.Text.Json;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class JsonStringVariableResolver
{
    internal static LiteralToken Tokenize(LiteralToken path, char escapeChar)
    {
        string physical = RenderToken(path);
        (Token Token, string Text)[] source = path.Tokens.Select(token => (token, RenderToken(token))).ToArray();
        List<(int Start, int End, Token Token, bool Inline)> references = new();
        HashSet<int> completeBraces = FindCompleteBracedReferences(physical, escapeChar);
        Dictionary<int, Result<VariableRefToken>> parsed = new();
        Result<VariableRefToken> ParseReference(TextSpan span) =>
            parsed.TryGetValue(span.Position.Absolute, out Result<VariableRefToken> result)
                ? result
                : Result.Empty<VariableRefToken>(span, "variable reference");
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
            if (VariableParsers.IsVariableReferenceStart(physical, start) &&
                (physical[start + 1] != '{' || completeBraces.Contains(start)))
            {
                candidates.Add(input);
            }
            input = input.Skip(1);
        }
        if (candidates.Count == 0)
        {
            return new LiteralToken(path.Tokens, canContainVariables: true, escapeChar)
            {
                QuoteChar = path.QuoteChar
            };
        }
        TextParser<VariableRefToken> parser = VariableRefToken.GetParser(escapeChar, ParseReference);
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
            if (VariableParsers.IsVariableReferenceStart(physical, start))
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
        HashSet<int> complete = new();
        if (physical.IndexOf("${", StringComparison.Ordinal) < 0)
        {
            return complete;
        }
        List<int> offsets = new();
        string logical = ConstructReader.FoldHeader(physical, 0, escapeChar, out _, offsets);
        Stack<int> openings = new();
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
        if (!expandVariables && !options.RemoveEscapeCharacters)
        {
            return physical;
        }
        string decoded = Decode(logical, offsets, out List<int> starts, out List<int> ends);
        Position PhysicalPosition(int decodedOffset) => new TextSpan(physical).Skip(
            decodedOffset < starts.Count ? starts[decodedOffset] : offsets[logical.Length - 1]).Position;
        List<(int Start, int End, Token? Custom)>? literalSpans = expandVariables ? new() : null;
        int sourcePosition = 1;
        (Token Token, string Text)[] source = path.Tokens.Select(token =>
        {
            string text = RenderToken(token, literalSpans, sourcePosition);
            sourcePosition += text.Length;
            return (token, text);
        }).ToArray();
        int[]? literalEnds = FindLiteralEnds(literalSpans, starts, ends, out var customTokens, out var emptyReferences);
        int[] resolvedCounts = new int[decoded.Length + 1];
        int sourceIndex = 0;
        int sourceStart = 1;
        for (int index = 0; index < decoded.Length; index++)
        {
            bool resolved = false;
            while (sourceIndex < source.Length && sourceStart < ends[index])
            {
                int sourceEnd = sourceStart + source[sourceIndex].Text.Length;
                resolved |= sourceEnd > starts[index] && path.IsResolvedJsonLiteral(source[sourceIndex].Token);
                if (sourceEnd > ends[index])
                {
                    break;
                }
                sourceStart = sourceEnd;
                sourceIndex++;
            }
            resolvedCounts[index + 1] = resolvedCounts[index] + (resolved ? 1 : 0);
        }

        List<(int Start, int End, Token Token, bool Inline)> replacements = new();
        Dictionary<Token, VariableRefToken>? emptyAnchors = null;
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
        TextParser<VariableRefToken>? variableParser = null;
        TextSpan input = new(decoded);
        int tokenIndex = 0;
        int tokenStart = 1;
        char? quote = null;
        while (true)
        {
            int index = input.Position.Absolute;
            if (quote != '\'' && emptyReferences is not null &&
                emptyReferences.TryGetValue(index, out var empty))
            {
                int position = index < starts.Count ? starts[index] : offsets[logical.Length - 1];
                foreach (VariableRefToken emptyReference in empty)
                {
                    string value = ResolveReference(emptyReference, escapeChar, variables, valueOptions, options);
                    StringToken substitution = new(Encode(value));
                    replacements.Add((position, position, substitution, true));
                    emptyAnchors ??= new(ReferenceComparer<Token>.Instance);
                    emptyAnchors.Add(substitution, emptyReference);
                }
            }
            if (input.IsAtEnd)
            {
                break;
            }
            while (tokenIndex < source.Length && tokenStart + source[tokenIndex].Text.Length <= starts[index])
            {
                tokenStart += source[tokenIndex].Text.Length;
                tokenIndex++;
            }
            if (quote != '\'' && customTokens is not null &&
                customTokens.TryGetValue(index, out var custom) && custom.Token is VariableRefToken reference)
            {
                string value = ResolveReference(reference, escapeChar, variables, valueOptions, options);
                replacements.Add((starts[index], ends[custom.End - 1], new StringToken(Encode(value)), true));
                input = input.Skip(custom.End - index);
                continue;
            }
            if (resolvedCounts[index + 1] != resolvedCounts[index] || literalEnds?[index] > index)
            {
                int end = literalEnds?[index] > index ? literalEnds[index] : index + 1;
                int tokenEnd = tokenStart + source[tokenIndex].Text.Length;
                while (end < decoded.Length && starts[end] < tokenEnd &&
                    resolvedCounts[end + 1] != resolvedCounts[end])
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
            if (expandVariables && VariableParsers.IsVariableReferenceStart(decoded, index))
            {
                blocked = false;
                variableParser ??= DecodedVariableParsers.Variable(escapeChar, character, decoded,
                    () => blocked = true, index, literalEnds, quote, customTokens, emptyReferences);
                Result<VariableRefToken> variable = variableParser(input);
                if (variable.HasValue &&
                    resolvedCounts[variable.Remainder.Position.Absolute] == resolvedCounts[index])
                {
                    int end = variable.Remainder.Position.Absolute;
                    if (!VariableRefToken.IsPatternModifier(variable.Value.Modifier))
                    {
                        string value = ResolveReference(variable.Value, escapeChar, variables, valueOptions, options);
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
            options.UpdateInline && inlineOnly ? substitutions : null, emptyAnchors);
        string result = "\"" + string.Concat(tokens.Select(RenderToken)) + "\"";
        if (options.UpdateInline && replacements.Any(replacement => replacement.Inline))
        {
            path.ReplaceWithTokens(inlineOnly
                ? tokens
                : BuildTokens(source, replacements.Where(replacement => replacement.Inline), physical.Length - 1,
                    substitutions, emptyAnchors));
            path.RecordResolvedJsonLiterals(substitutions);
        }
        return result;
    }

    private static int[]? FindLiteralEnds(List<(int Start, int End, Token? Custom)>? spans,
        List<int> starts, List<int> ends, out Dictionary<int, (int End, Token Token)>? customTokens,
        out Dictionary<int, List<VariableRefToken>>? emptyReferences)
    {
        customTokens = null;
        emptyReferences = null;
        if (spans is null || spans.Count == 0)
        {
            return null;
        }
        int[] literalEnds = new int[starts.Count];
        int index = 0;
        foreach ((int start, int end, Token? custom) in spans)
        {
            while (index < ends.Count && ends[index] <= start)
            {
                index++;
            }
            int first = index;
            while (index < starts.Count && starts[index] < end)
            {
                index++;
            }
            if (start == end && custom is VariableRefToken reference)
            {
                emptyReferences ??= new();
                if (!emptyReferences.TryGetValue(first, out var references))
                {
                    references = new();
                    emptyReferences.Add(first, references);
                }
                references.Add(reference);
            }
            else if (custom is not null)
            {
                customTokens ??= new();
                customTokens.Add(first, (index, custom));
            }
            for (int current = first; current < index; current++)
            {
                literalEnds[current] = index;
            }
        }
        return literalEnds;
    }

    internal static string ResolveReference(VariableRefToken root, char escapeChar,
        IDictionary<string, string?> variables, ResolutionOptions options, ResolutionOptions? customOptions = null)
    {
        if (root.HasCustomResolution)
        {
            return root.ResolveVariables(escapeChar, variables,
                customOptions ?? options) ?? string.Empty;
        }
        if (root.ModifierValueToken is null || root.GetRequiredModifierValue(variables) is null)
        {
            return root.ResolveVariables(escapeChar, variables, options) ?? string.Empty;
        }
        Stack<Expansion> resolved = new();
        Stack<(VariableRefToken? Reference, Token[]? Tokens, string? RequiredName)> pending = new();
        pending.Push((root, null, null));
        while (pending.Count > 0)
        {
            var frame = pending.Pop();
            if (frame.Tokens is Token[] tokens)
            {
                Expansion[] children = new Expansion[tokens.Length];
                for (int index = 0; index < tokens.Length; index++)
                {
                    children[index] = tokens[index] is VariableRefToken
                        ? resolved.Pop()
                        : new Expansion(options.FormatValue(escapeChar, tokens[index].ToString()));
                }
                Expansion value = new(children, options.RemoveEscapeCharacters);
                if (frame.RequiredName is string name)
                {
                    throw VariableRefToken.MissingVariable(name,
                        RenderExpansion(value, escapeChar, formatRoot: false));
                }
                resolved.Push(value);
                continue;
            }
            VariableRefToken reference = frame.Reference!;
            if (reference.HasCustomResolution)
            {
                resolved.Push(new Expansion(reference.ResolveVariables(escapeChar, variables,
                    customOptions ?? options) ?? string.Empty));
                continue;
            }
            if (VariableRefToken.IsPatternModifier(reference.Modifier))
            {
                resolved.Push(new Expansion(RenderToken(reference)));
                continue;
            }
            LiteralToken? modifier = reference.GetRequiredModifierValue(variables);
            if (modifier is null)
            {
                resolved.Push(new Expansion(reference.ResolveVariables(escapeChar, variables, options)
                    ?? string.Empty));
            }
            else if (modifier.HasCustomResolution)
            {
                resolved.Push(new Expansion(reference.ResolveVariables(escapeChar, variables,
                    customOptions ?? options) ?? string.Empty));
            }
            else
            {
                Token[] children = modifier.Tokens.ToArray();
                pending.Push((null, children, reference.Modifier!.Last() == '?' ? reference.VariableName : null));
                foreach (VariableRefToken child in children.OfType<VariableRefToken>())
                {
                    pending.Push((child, null, null));
                }
            }
        }
        return RenderExpansion(resolved.Pop(), escapeChar);
    }

    private sealed class Expansion
    {
        internal Expansion(string text) => Text = text;
        internal Expansion(Expansion[] children, bool format)
        {
            Children = children;
            Format = format;
        }
        internal string? Text { get; }
        internal Expansion[]? Children { get; }
        internal bool Format { get; }
    }

    private static string RenderExpansion(Expansion root, char escapeChar, bool formatRoot = true)
    {
        StringBuilder builder = new();
        List<long> escapes = new();
        // A non-escape resets every formatting layer without walking the ancestor stack.
        long epoch = 1;
        Stack<(Expansion Value, bool Close, bool Format)> pending = new();
        pending.Push((root, false, root.Format && formatRoot));
        while (pending.Count > 0)
        {
            (Expansion value, bool close, bool format) = pending.Pop();
            if (close)
            {
                escapes.RemoveAt(escapes.Count - 1);
            }
            else if (value.Text is string text)
            {
                if (escapes.Count == 0 || text.Length > 0 && text.IndexOf(escapeChar) < 0)
                {
                    builder.Append(text);
                    if (text.Length > 0)
                    {
                        epoch++;
                    }
                    continue;
                }
                foreach (char character in text)
                {
                    if (character != escapeChar)
                    {
                        epoch++;
                        builder.Append(character);
                        continue;
                    }
                    bool emit = true;
                    for (int index = escapes.Count - 1; index >= 0; index--)
                    {
                        if (escapes[index] != epoch)
                        {
                            escapes[index] = epoch;
                            emit = false;
                            break;
                        }
                        escapes[index] = 0;
                    }
                    if (emit)
                    {
                        builder.Append(character);
                    }
                }
            }
            else
            {
                if (format)
                {
                    escapes.Add(0);
                    pending.Push((value, true, false));
                }
                foreach (Expansion child in value.Children!.Reverse())
                {
                    pending.Push((child, false, child.Format));
                }
            }
        }
        return builder.ToString();
    }

    internal static string RenderToken(Token token) => RenderToken(token, null, 0);

    private static bool HasDefaultRendering(Token token)
    {
        if (token.GetType() == typeof(VariableRefToken) || token.GetType() == typeof(LiteralToken))
        {
            return true;
        }
        return token is LiteralToken literal && literal.HasDefaultRendering;
    }

    private static string RenderToken(Token token, List<(int Start, int End, Token? Custom)>? literalSpans,
        int offset)
    {
        if (!HasDefaultRendering(token) && (literalSpans is null ||
            token is not Tokens.LiteralToken && token is not VariableRefToken { HasCustomResolution: true }))
        {
            return token.ToString();
        }
        StringBuilder builder = new();
        Stack<(Token Token, bool Close)> pending = new();
        pending.Push((token, false));
        while (pending.Count > 0)
        {
            (Token current, bool close) = pending.Pop();
            if (close)
            {
                builder.Append(((IQuotableToken)current).QuoteChar);
            }
            else if (literalSpans is not null && current is LiteralToken literal &&
                (!literal.CanContainVariables || literal.HasCustomResolution))
            {
                int start = builder.Length;
                builder.Append(RenderToken(literal));
                bool custom = literal.HasCustomResolution;
                if (builder.Length > start || custom)
                {
                    literalSpans.Add((offset + start, offset + builder.Length, custom ? literal : null));
                }
            }
            else if (literalSpans is not null && current is VariableRefToken reference && reference.HasCustomResolution)
            {
                int start = builder.Length;
                builder.Append(RenderToken(reference));
                literalSpans.Add((offset + start, offset + builder.Length, reference));
            }
            else if (current is AggregateToken aggregate && HasDefaultRendering(current))
            {
                if (current is VariableRefToken)
                {
                    builder.Append('$');
                }
                if (current is IQuotableToken quotable && quotable.QuoteChar.HasValue)
                {
                    builder.Append(quotable.QuoteChar);
                    pending.Push((current, true));
                }
                foreach (Token child in aggregate.Tokens.Reverse())
                {
                    pending.Push((child, false));
                }
            }
            else
            {
                builder.Append(current.ToString());
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
        List<Token>? substitutions = null, Dictionary<Token, VariableRefToken>? emptyAnchors = null)
    {
        if (emptyAnchors is not null)
        {
            List<(Token Token, string Text)> expanded = new();
            Stack<(Token Token, string Text)> pending = new(source.Reverse());
            while (pending.Count > 0)
            {
                var item = pending.Pop();
                if (item.Text.Length == 0 &&
                    item.Token is LiteralToken
                    {
                        CanContainVariables: true, HasCustomResolution: false,
                        HasDefaultRendering: true, QuoteChar: null
                    } literal)
                {
                    foreach (Token child in literal.Tokens.Reverse())
                    {
                        pending.Push((child, RenderToken(child)));
                    }
                }
                else
                {
                    expanded.Add(item);
                }
            }
            source = expanded.ToArray();
        }
        List<Token> tokens = new();
        int tokenIndex = 0;
        int tokenStart = 1;
        void CopyUnchanged(int start, int stop, VariableRefToken? anchor = null)
        {
            while (tokenIndex < source.Length &&
                (tokenStart < stop || tokenStart == stop && source[tokenIndex].Text.Length == 0))
            {
                (Token token, string text) = source[tokenIndex];
                if (tokenStart == stop && ReferenceEquals(token, anchor))
                {
                    break;
                }
                int tokenEnd = tokenStart + text.Length;
                int first = Math.Max(start - tokenStart, 0);
                int last = Math.Min(stop - tokenStart, text.Length);
                if (first < last || text.Length == 0 && tokenStart >= start)
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
            VariableRefToken? anchor = null;
            emptyAnchors?.TryGetValue(substitution, out anchor);
            CopyUnchanged(position, start, anchor);
            if (anchor is not null && tokenIndex < source.Length &&
                ReferenceEquals(source[tokenIndex].Token, anchor))
            {
                tokenIndex++;
            }
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
