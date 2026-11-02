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
        List<(int Start, int End, Token? Custom, bool Modifier)>? literalSpans = expandVariables ? new() : null;
        int sourcePosition = 1;
        (Token Token, string Text)[] source = path.Tokens.Select(token =>
        {
            string text = RenderToken(token, literalSpans, sourcePosition);
            sourcePosition += text.Length;
            return (token, text);
        }).ToArray();
        int[]? literalEnds = FindLiteralEnds(literalSpans, starts, ends, out var customTokens,
            out var emptyReferences, out var customModifiers);
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

        List<(int Start, int End, AggregateToken? Reference)> pendingReplacements = new();
        void SubstituteReference(int start, int end, AggregateToken reference) =>
            pendingReplacements.Add((start, end, reference));
        void QueueFormatting(int start, int end) => pendingReplacements.Add((start, end, null));
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
                foreach (AggregateToken emptyReference in empty)
                {
                    SubstituteReference(position, position, emptyReference);
                }
            }
            if (quote != '\'' && customTokens is not null &&
                customTokens.TryGetValue(index, out var custom))
            {
                foreach (AggregateToken customToken in custom.Tokens)
                {
                    SubstituteReference(starts[index], ends[custom.End - 1], customToken);
                }
                input = input.Skip(custom.End - index);
                continue;
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
            if (resolvedCounts[index + 1] != resolvedCounts[index] || literalEnds?[index] > index)
            {
                int end = literalEnds?[index] > index ? literalEnds[index] : index + 1;
                int tokenEnd = tokenStart + source[tokenIndex].Text.Length;
                while (end < decoded.Length && starts[end] < tokenEnd &&
                    resolvedCounts[end + 1] != resolvedCounts[end])
                {
                    end++;
                }
                if (decoded.IndexOf(escapeChar, index, end - index) >= 0)
                {
                    QueueFormatting(starts[index], ends[end - 1]);
                }
                input = input.Skip(end - index);
                continue;
            }
            char current = decoded[index];
            if (quote == '\'')
            {
                if (current == escapeChar)
                {
                    int end = index + 1;
                    if (end < decoded.Length && decoded[end] == escapeChar)
                    {
                        end++;
                    }
                    QueueFormatting(starts[index], ends[end - 1]);
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
                    QueueFormatting(starts[index], ends[index]);
                    input = input.Skip(1);
                    continue;
                }
                int length = char.IsHighSurrogate(decoded[index + 1]) &&
                    index + 2 < decoded.Length && char.IsLowSurrogate(decoded[index + 2]) ? 2 : 1;
                QueueFormatting(starts[index], ends[index + length]);
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
                    () => blocked = true, index, literalEnds, quote, customTokens, emptyReferences, customModifiers);
                Result<VariableRefToken> variable = variableParser(input);
                if (variable.HasValue &&
                    resolvedCounts[variable.Remainder.Position.Absolute] == resolvedCounts[index])
                {
                    int end = variable.Remainder.Position.Absolute;
                    if (!VariableRefToken.IsPatternModifier(variable.Value.Modifier))
                    {
                        SubstituteReference(starts[index], ends[end - 1], variable.Value);
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
        Token?[] resolvedReplacements = new Token?[pendingReplacements.Count];
        for (int index = pendingReplacements.Count - 1; index >= 0; index--)
        {
            var pending = pendingReplacements[index];
            string value;
            if (pending.Reference is not null)
            {
                valueOptions.RemoveEscapeCharacters = options.RemoveEscapeCharacters;
                value = pending.Reference is LiteralToken literal
                    ? literal.ResolveVariablesInContext(false, escapeChar, variables, options) ?? string.Empty
                    : ResolveReference((VariableRefToken)pending.Reference, escapeChar, variables, valueOptions, options);
            }
            else
            {
                if (!options.RemoveEscapeCharacters)
                {
                    continue;
                }
                int first = starts.BinarySearch(pending.Start);
                int last = ends.BinarySearch(pending.End);
                string literal = decoded.Substring(first, last - first + 1);
                value = options.FormatValue(escapeChar, literal);
                if (value == literal)
                {
                    continue;
                }
            }
            resolvedReplacements[index] = new StringToken(Encode(value));
        }
        List<(int Start, int End, Token Token, bool Inline)> replacements = new();
        Dictionary<Token, AggregateToken>? emptyAnchors = null;
        for (int index = 0; index < pendingReplacements.Count; index++)
        {
            if (resolvedReplacements[index] is Token substitution)
            {
                var pending = pendingReplacements[index];
                replacements.Add((pending.Start, pending.End, substitution, pending.Reference is not null));
                if (pending.Start == pending.End && pending.Reference is AggregateToken reference)
                {
                    emptyAnchors ??= new(ReferenceComparer<Token>.Instance);
                    emptyAnchors.Add(substitution, reference);
                }
            }
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

    private static int[]? FindLiteralEnds(List<(int Start, int End, Token? Custom, bool Modifier)>? spans,
        List<int> starts, List<int> ends, out Dictionary<int, (int End, List<AggregateToken> Tokens)>? customTokens,
        out Dictionary<int, List<AggregateToken>>? emptyReferences,
        out Dictionary<int, LiteralToken>? customModifiers)
    {
        customTokens = null;
        emptyReferences = null;
        customModifiers = null;
        if (spans is null || spans.Count == 0)
        {
            return null;
        }
        int[] literalEnds = new int[starts.Count];
        int first = 0;
        int customStart = -1;
        int customEnd = -1;
        foreach ((int start, int end, Token? custom, bool modifier) in spans)
        {
            while (first < ends.Count && ends[first] <= start)
            {
                first++;
            }
            int index = first;
            while (index < starts.Count && starts[index] < end)
            {
                index++;
            }
            if (modifier && custom is LiteralToken literal)
            {
                customModifiers ??= new();
                customModifiers.Add(first, literal);
            }
            if (start == end && custom is AggregateToken reference)
            {
                if (first < customEnd)
                {
                    customTokens![customStart].Tokens.Add(reference);
                    continue;
                }
                emptyReferences ??= new();
                if (!emptyReferences.TryGetValue(first, out var references))
                {
                    references = new();
                    emptyReferences.Add(first, references);
                }
                references.Add(reference);
            }
            else if (custom is AggregateToken customToken)
            {
                customTokens ??= new();
                // Token boundaries can split one JSON escape, so decoded custom spans can overlap.
                if (first < customEnd)
                {
                    customTokens[customStart].Tokens.Add(customToken);
                    customTokens[customStart] = (index, customTokens[customStart].Tokens);
                }
                else
                {
                    customStart = first;
                    customTokens.Add(first, (index, new() { customToken }));
                }
                customEnd = index;
                for (int current = customStart; current < index; current++)
                {
                    literalEnds[current] = index;
                }
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
        Stack<(Token? Token, Token[]? Tokens, string? RequiredName)> pending = new();
        pending.Push((root, null, null));
        while (pending.Count > 0)
        {
            var frame = pending.Pop();
            if (customOptions is not null)
            {
                options.RemoveEscapeCharacters = customOptions.RemoveEscapeCharacters;
            }
            if (frame.Tokens is Token[] tokens)
            {
                Expansion[] children = new Expansion[tokens.Length];
                for (int index = 0; index < tokens.Length; index++)
                {
                    children[index] = resolved.Pop();
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
            if (frame.Token is LiteralToken customLiteral)
            {
                resolved.Push(new Expansion(customLiteral.ResolveVariablesInContext(
                    false, escapeChar, variables, customOptions ?? options) ?? string.Empty));
                continue;
            }
            if (frame.Token is not VariableRefToken reference)
            {
                resolved.Push(new Expansion(options.FormatValue(escapeChar, frame.Token!.ToString())));
                continue;
            }
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
                foreach (Token child in children)
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

    private static string RenderToken(Token token, List<(int Start, int End, Token? Custom, bool Modifier)>? literalSpans,
        int offset)
    {
        if (!HasDefaultRendering(token) && (literalSpans is null ||
            token is not Tokens.LiteralToken && token is not VariableRefToken { HasCustomResolution: true }))
        {
            return token.ToString();
        }
        StringBuilder builder = new();
        Stack<(Token Token, bool Close, bool Modifier)> pending = new();
        pending.Push((token, false, false));
        while (pending.Count > 0)
        {
            (Token current, bool close, bool modifier) = pending.Pop();
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
                    literalSpans.Add((offset + start, offset + builder.Length, custom ? literal : null, modifier));
                }
            }
            else if (literalSpans is not null && current is VariableRefToken reference && reference.HasCustomResolution)
            {
                int start = builder.Length;
                builder.Append(RenderToken(reference));
                literalSpans.Add((offset + start, offset + builder.Length, reference, false));
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
                    pending.Push((current, true, false));
                }
                LiteralToken? modifierValue = literalSpans is not null && current is VariableRefToken variable
                    ? variable.ModifierValueToken : null;
                foreach (Token child in aggregate.Tokens.Reverse())
                {
                    pending.Push((child, false, ReferenceEquals(child, modifierValue)));
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

    private sealed class SourcePart(Token token, string? text = null)
    {
        internal Token Token { get; } = token;
        internal string? Text { get; set; } = text;
        internal List<SourcePart>? Children { get; set; }
        internal bool ContainsReplacement { get; set; }
    }

    private static (Token Token, string Text)[] ExposeReplacedDescendants(
        (Token Token, string Text)[] source,
        IEnumerable<(int Start, int End, Token Token, bool Inline)> replacements,
        Dictionary<Token, AggregateToken>? emptyAnchors)
    {
        Dictionary<int, HashSet<Token>> anchors = new();
        foreach (var replacement in replacements)
        {
            if (emptyAnchors?.TryGetValue(replacement.Token, out AggregateToken? anchor) == true)
            {
                if (!anchors.TryGetValue(replacement.Start, out var tokens))
                {
                    tokens = new(ReferenceComparer<Token>.Instance);
                    anchors.Add(replacement.Start, tokens);
                }
                tokens.Add(anchor);
            }
        }
        SourcePart[] roots = source.Select(item => new SourcePart(item.Token, item.Text)).ToArray();
        var ranges = replacements.Where(replacement => replacement.Start < replacement.End).ToArray();
        int rangeIndex = 0;
        Stack<(SourcePart Part, bool Close)> pending = new(roots.Reverse().Select(part => (part, false)));
        int position = 1;
        while (pending.Count > 0)
        {
            (SourcePart part, bool close) = pending.Pop();
            if (close)
            {
                part.ContainsReplacement = part.Children!.Any(child => child.ContainsReplacement);
            }
            else if (part.Token is LiteralToken
                {
                    CanContainVariables: true, HasCustomResolution: false,
                    HasDefaultRendering: true
                } literal)
            {
                part.Children = literal.Tokens.Select(child => new SourcePart(child)).ToList();
                if (literal.QuoteChar is char quote)
                {
                    part.Children.Insert(0, new SourcePart(new SymbolToken(quote), quote.ToString()));
                    part.Children.Add(new SourcePart(new SymbolToken(quote), quote.ToString()));
                }
                pending.Push((part, true));
                foreach (SourcePart child in part.Children.AsEnumerable().Reverse())
                {
                    pending.Push((child, false));
                }
            }
            else
            {
                part.Text ??= RenderToken(part.Token);
                while (rangeIndex < ranges.Length && ranges[rangeIndex].End <= position)
                {
                    rangeIndex++;
                }
                part.ContainsReplacement = part.Text.Length == 0 &&
                    anchors.TryGetValue(position, out var tokens) && tokens.Contains(part.Token);
                part.ContainsReplacement |= part.Text.Length > 0 && rangeIndex < ranges.Length &&
                    ranges[rangeIndex].Start < position + part.Text.Length;
                position += part.Text.Length;
            }
        }
        List<(Token Token, string Text)> expanded = new();
        pending = new(roots.Reverse().Select(part => (part, false)));
        while (pending.Count > 0)
        {
            SourcePart part = pending.Pop().Part;
            if (part.ContainsReplacement && part.Children is { } children)
            {
                foreach (SourcePart child in children.AsEnumerable().Reverse())
                {
                    pending.Push((child, false));
                }
            }
            else
            {
                expanded.Add((part.Token, part.Text ?? RenderToken(part.Token)));
            }
        }
        return expanded.ToArray();
    }

    private static List<Token> BuildTokens((Token Token, string Text)[] source,
        IEnumerable<(int Start, int End, Token Token, bool Inline)> replacements, int end,
        List<Token>? substitutions = null, Dictionary<Token, AggregateToken>? emptyAnchors = null)
    {
        if (substitutions is not null)
        {
            source = ExposeReplacedDescendants(source, replacements, emptyAnchors);
        }
        List<Token> tokens = new();
        int tokenIndex = 0;
        int tokenStart = 1;
        void CopyUnchanged(int start, int stop, AggregateToken? anchor = null)
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
            AggregateToken? anchor = null;
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
