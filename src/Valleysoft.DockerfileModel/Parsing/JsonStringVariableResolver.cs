using System.Text;
using System.Text.Json;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class JsonStringVariableResolver
{
    internal static LiteralToken Tokenize(LiteralToken path, char escapeChar)
    {
        string physical = RenderToken(path);
        (Token Token, string Text, int Occurrence)[] source = path.Tokens.Select(token => (token, RenderToken(token), -1)).ToArray();
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
        List<(int Start, int End, Token? Custom, bool Modifier, int Occurrence)>? literalSpans = expandVariables ? new() : null;
        Dictionary<int, (int End, VariableRefToken Token, bool Compatible, AggregateToken? Parent, int Index)>? sourceReferences =
            expandVariables && (options.UpdateInline || path.HasCustomDescendant()) ? new() : null;
        using SourceSlots? sourceSlots = sourceReferences is not null ? new() : null;
        Dictionary<VariableRefToken, ReferenceSource>? referenceSources = null;
        SourceSnapshot? snapshot = expandVariables && (options.UpdateInline || sourceReferences is not null)
            ? new(physical) : null;
        int sourcePosition = 1;
        (Token Token, string Text, int Occurrence)[] source = path.Tokens.Select((token, index) =>
        {
            int occurrence = snapshot?.Occurrences.Count ?? -1;
            string text = RenderToken(token, literalSpans, sourcePosition, sourceReferences, snapshot?.Occurrences,
                path, index, sourceSlots);
            sourcePosition += text.Length;
            return (token, text, occurrence);
        }).ToArray();
        int[]? literalEnds = FindLiteralEnds(literalSpans, starts, ends, out var customTokens,
            out var emptyReferences, out var customModifiers);
        int[] resolvedCounts = new int[decoded.Length + 1];
        int[]? bindingBarriers = sourceReferences is not null ? new int[decoded.Length + 1] : null;
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
            if (bindingBarriers is not null)
            {
                char value = decoded[index];
                bool barrier = ends[index] - starts[index] != 1 || physical[starts[index]] != value ||
                    value == escapeChar || value is '\'' or '"';
                bindingBarriers[index + 1] = bindingBarriers[index] + (barrier ? 1 : 0);
            }
        }

        List<(int Start, int End, AggregateToken? Reference)> pendingReplacements = new();
        Dictionary<int, (int Position, int Occurrence)>? pendingAnchors = null;
        void SubstituteReference(int start, int end, AggregateToken reference, int anchorPosition = -1, int occurrence = -1)
        {
            if (start == end && occurrence >= 0)
            {
                pendingAnchors ??= new();
                pendingAnchors.Add(pendingReplacements.Count, (anchorPosition, occurrence));
            }
            pendingReplacements.Add((start, end, reference));
        }
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
        VariableRefToken BindReference(int start, int end, VariableRefToken parsed)
        {
            int first = starts[start];
            int last = ends[end - 1];
            if (sourceReferences?.TryGetValue(first, out var original) == true && original.End == last)
            {
                bool live = last - first == end - start &&
                    bindingBarriers![end] == bindingBarriers[start] && original.Compatible;
                referenceSources ??= new(ReferenceComparer<VariableRefToken>.Instance);
                referenceSources.Add(parsed, new(original.Token, live, sourceSlots!.Get(original.Parent, original.Index)));
            }
            return parsed;
        }
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
                foreach (var emptyReference in empty)
                {
                    SubstituteReference(emptyReference.Start, emptyReference.End, emptyReference.Token,
                        emptyReference.Position, emptyReference.Occurrence);
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
                    () => blocked = true, index, literalEnds, quote, customTokens, emptyReferences, customModifiers,
                    sourceReferences is not null ? BindReference : null);
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
        bool[] inlineReplacements = new bool[pendingReplacements.Count];
        for (int index = pendingReplacements.Count - 1; index >= 0; index--)
        {
            var pending = pendingReplacements[index];
            string value;
            if (pending.Reference is not null)
            {
                valueOptions.RemoveEscapeCharacters = options.RemoveEscapeCharacters;
                value = pending.Reference switch
                {
                    LiteralToken literal => literal.ResolveVariablesInContext(false, escapeChar, variables, options)
                        ?? string.Empty,
                    VariableRefToken reference => ResolveReference(reference, escapeChar, variables, valueOptions, options,
                        referenceSources),
                    _ => pending.Reference.ResolveVariables(escapeChar, variables, options) ?? string.Empty
                };
                inlineReplacements[index] = options.UpdateInline;
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
        Dictionary<Token, (int Position, int Occurrence)>? emptyAnchors = null;
        for (int index = 0; index < pendingReplacements.Count; index++)
        {
            if (resolvedReplacements[index] is Token substitution)
            {
                var pending = pendingReplacements[index];
                replacements.Add((pending.Start, pending.End, substitution, inlineReplacements[index]));
                if (pending.Start == pending.End && pending.Reference is AggregateToken &&
                    pendingAnchors?.TryGetValue(index, out var anchor) == true)
                {
                    emptyAnchors ??= new(ReferenceComparer<Token>.Instance);
                    emptyAnchors.Add(substitution, anchor);
                }
            }
        }
        if (replacements.Count == 0)
        {
            return physical;
        }
        List<Token> substitutions = new();
        List<Token> tokens = BuildTokens(source, replacements, physical.Length - 1, emptyAnchors: emptyAnchors,
            snapshot: sourceReferences is not null ? snapshot : null);
        string result = "\"" + string.Concat(tokens.Select(RenderToken)) + "\"";
        if (replacements.Any(replacement => replacement.Inline))
        {
            path.ReplaceWithTokens(BuildTokens(source, replacements.Where(replacement => replacement.Inline),
                physical.Length - 1, substitutions, emptyAnchors, snapshot));
            path.RecordResolvedJsonLiterals(substitutions);
        }
        return result;
    }

    private static int[]? FindLiteralEnds(List<(int Start, int End, Token? Custom, bool Modifier, int Occurrence)>? spans,
        List<int> starts, List<int> ends, out Dictionary<int, (int End, List<AggregateToken> Tokens)>? customTokens,
        out Dictionary<int, List<(AggregateToken Token, int Start, int End, int Position, int Occurrence)>>? emptyReferences,
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
        foreach ((int start, int end, Token? custom, bool modifier, int occurrence) in spans)
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
            if ((start == end || first == index) && custom is AggregateToken reference)
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
                int insertion = start == end && first < starts.Count && starts[first] < start
                    ? starts[first] : start;
                references.Add((reference, insertion, start == end ? insertion : end, start, occurrence));
                continue;
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
        IDictionary<string, string?> variables, ResolutionOptions options, ResolutionOptions? customOptions = null,
        Dictionary<VariableRefToken, ReferenceSource>? referenceSources = null)
    {
        Token rootValue = GetSourceToken(root, referenceSources);
        if (rootValue is not VariableRefToken { HasCustomResolution: false } rootReference)
        {
            return ResolveSourceToken(rootValue, escapeChar, variables, customOptions ?? options);
        }
        if (rootReference.ModifierValueToken is null || rootReference.GetRequiredModifierValue(variables) is null)
        {
            return rootReference.ResolveVariables(escapeChar, variables, options) ?? string.Empty;
        }
        Stack<Expansion> resolved = new();
        Stack<(Token? Token, Token[]? Tokens, string? RequiredName, AggregateToken? Parent, int Index)> pending = new();
        pending.Push((root, null, null, null, -1));
        void Complete(Expansion value, Token? token, AggregateToken? parent, int index, bool remove = false)
        {
            resolved.Push(value);
            if (token is VariableRefToken reference && parent is not null && (customOptions ?? options).UpdateInline)
            {
                ReferenceSource? source = reference is CustomReferenceOccurrence custom ? custom.Source :
                    referenceSources?.TryGetValue(reference, out var bound) == true ? bound : null;
                if (source?.Slot is SourceSlot location)
                {
                    if (location.Removed)
                    {
                        return;
                    }
                    parent = location.Parent;
                    index = location.Index;
                }
                if (remove)
                {
                    parent.RemoveResolvedChild(index);
                }
                else
                {
                    parent.TokenList[index] = customOptions is null
                        ? new StringToken(value.Text ?? RenderExpansion(value, escapeChar))
                        : new ResolvedJsonLiteral(value, escapeChar);
                }
            }
        }
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
                Expansion value = new(children, frame.Token is VariableRefToken && options.RemoveEscapeCharacters);
                if (frame.RequiredName is string name)
                {
                    throw VariableRefToken.MissingVariable(name,
                        RenderExpansion(value, escapeChar, formatRoot: false));
                }
                Complete(value, frame.Token, frame.Parent, frame.Index);
                continue;
            }
            if (frame.Parent is not null)
            {
                frame.Token = frame.Parent.TokenList[frame.Index];
            }
            Token? syntax = frame.Token;
            if (syntax is VariableRefToken parsed)
            {
                frame.Token = GetSourceToken(parsed, referenceSources);
            }
            if (frame.Token is AggregateToken container && IsTransparentContainer(container) &&
                container is not IQuotableToken { QuoteChar: not null })
            {
                Token[] children = container.Tokens.ToArray();
                pending.Push((container, children, null, frame.Parent, frame.Index));
                for (int index = 0; index < children.Length; index++)
                {
                    pending.Push((children[index], null, null, container, index));
                }
                continue;
            }
            if (frame.Token is LiteralToken customLiteral)
            {
                resolved.Push(new Expansion(ResolveSourceToken(customLiteral,
                    escapeChar, variables, customOptions ?? options)));
                continue;
            }
            if (frame.Token is not VariableRefToken reference)
            {
                resolved.Push(new Expansion(frame.Token is AggregateToken aggregate
                    ? aggregate.ResolveVariables(escapeChar, variables, customOptions ?? options) ?? string.Empty
                    : options.FormatValue(escapeChar, frame.Token!.ToString())));
                continue;
            }
            if (reference.HasCustomResolution)
            {
                string? value = reference.ResolveVariables(escapeChar, variables, customOptions ?? options);
                Complete(new Expansion(value ?? string.Empty), syntax, frame.Parent, frame.Index, value is null);
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
                Complete(new Expansion(reference.ResolveVariables(escapeChar, variables, options)
                    ?? string.Empty), syntax, frame.Parent, frame.Index);
            }
            else if (modifier.HasCustomResolution)
            {
                string? value = reference.ResolveVariables(escapeChar, variables, customOptions ?? options);
                Complete(new Expansion(value ?? string.Empty), syntax, frame.Parent, frame.Index, value is null);
            }
            else
            {
                Token[] children = modifier.Tokens.ToArray();
                pending.Push((syntax, children, reference.Modifier!.Last() == '?' ? reference.VariableName : null,
                    frame.Parent, frame.Index));
                for (int index = 0; index < children.Length; index++)
                {
                    pending.Push((children[index], null, null, modifier, index));
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
        return token switch
        {
            LiteralToken literal => literal.HasDefaultRendering,
            VariableRefToken reference => reference.HasDefaultRendering,
            AggregateToken aggregate => aggregate.HasDefaultAggregateRendering,
            _ => false
        };
    }

    private static string RenderToken(Token token, List<(int Start, int End, Token? Custom, bool Modifier, int Occurrence)>? literalSpans,
        int offset, Dictionary<int, (int End, VariableRefToken Token, bool Compatible, AggregateToken? Parent, int Index)>? sourceReferences = null,
        List<SourceOccurrence>? occurrences = null, AggregateToken? ownerParent = null, int ownerIndex = -1,
        SourceSlots? sourceSlots = null)
    {
        if (!HasDefaultRendering(token) && (literalSpans is null ||
            token is not Tokens.LiteralToken && token is not AggregateToken { HasCustomVariableResolution: true }))
        {
            string text = token.ToString();
            occurrences?.Add(new(token, offset, offset + text.Length, occurrences.Count + 1, false,
                sourceSlots?.Get(ownerParent, ownerIndex)));
            return text;
        }
        StringBuilder builder = new();
        Stack<(Token Token, bool Close, bool Modifier, int Start, int RenderingVersion, int Occurrence, bool Capture,
            AggregateToken? Parent, int Index)> pending = new();
        pending.Push((token, false, false, 0, 0, -1, occurrences is not null, ownerParent, ownerIndex));
        int renderingVersion = 0;
        while (pending.Count > 0)
        {
            (Token current, bool close, bool modifier, int opening, int openingVersion, int occurrence, bool capture,
                AggregateToken? parent, int childIndex) = pending.Pop();
            if (!close && capture)
            {
                occurrence = occurrences!.Count;
                occurrences.Add(new(current, offset + builder.Length, 0, 0, IsTransparentContainer(current),
                    sourceSlots?.Get(parent, childIndex)));
            }
            if (!close && current is AggregateToken && !HasDefaultRendering(current))
            {
                bool opaqueResolution = current switch
                {
                    VariableRefToken reference => reference.HasCustomResolution,
                    LiteralToken literal => literal.HasCustomResolution,
                    AggregateToken aggregate => aggregate.HasCustomVariableResolution,
                    _ => false
                };
                if (!opaqueResolution)
                {
                    renderingVersion++;
                }
            }
            if (close)
            {
                if (current is IQuotableToken quotable)
                {
                    if (occurrence >= 0 && occurrences![occurrence].Transparent && quotable.QuoteChar is char quote)
                    {
                        occurrences!.Add(new(new SymbolToken(quote), offset + builder.Length,
                            offset + builder.Length + 1, occurrences.Count + 1, false));
                    }
                    builder.Append(quotable.QuoteChar);
                }
                if (current is VariableRefToken reference)
                {
                    sourceReferences?.Add(offset + opening,
                        (offset + builder.Length, reference, renderingVersion == openingVersion, parent, childIndex));
                }
                if (occurrence >= 0)
                {
                    SourceOccurrence original = occurrences![occurrence];
                    occurrences[occurrence] = new(current, original.Start, offset + builder.Length,
                        occurrences.Count, original.Transparent, original.Slot);
                }
            }
            else if (literalSpans is not null && current is LiteralToken literal &&
                (!literal.CanContainVariables || literal.HasCustomResolution))
            {
                int start = builder.Length;
                builder.Append(RenderToken(literal));
                bool custom = literal.HasCustomResolution;
                if (builder.Length > start || custom)
                {
                    literalSpans.Add((offset + start, offset + builder.Length, custom ? literal : null, modifier, occurrence));
                }
            }
            else if (literalSpans is not null && current is VariableRefToken reference && reference.HasCustomResolution)
            {
                int start = builder.Length;
                builder.Append(RenderToken(reference));
                literalSpans.Add((offset + start, offset + builder.Length,
                    new CustomReferenceOccurrence(new(reference, true, sourceSlots?.Get(parent, childIndex))), false, occurrence));
            }
            else if (literalSpans is not null &&
                current is AggregateToken { HasCustomVariableResolution: true } and not Tokens.LiteralToken and not VariableRefToken)
            {
                int start = builder.Length;
                builder.Append(RenderToken(current));
                literalSpans.Add((offset + start, offset + builder.Length, current, false, occurrence));
            }
            else if (current is AggregateToken aggregate && HasDefaultRendering(current))
            {
                int start = builder.Length;
                if (current is VariableRefToken)
                {
                    builder.Append('$');
                }
                if (current is IQuotableToken quotable && quotable.QuoteChar.HasValue)
                {
                    if (occurrence >= 0 && occurrences![occurrence].Transparent)
                    {
                        occurrences!.Add(new(new SymbolToken(quotable.QuoteChar.Value), offset + builder.Length,
                            offset + builder.Length + 1, occurrences.Count + 1, false));
                    }
                    builder.Append(quotable.QuoteChar);
                }
                if (current is IQuotableToken { QuoteChar: not null } ||
                    current is VariableRefToken && sourceReferences is not null || occurrence >= 0)
                {
                    pending.Push((current, true, false, start, renderingVersion, occurrence, false, parent, childIndex));
                }
                LiteralToken? modifierValue = literalSpans is not null && current is VariableRefToken variable
                    ? variable.ModifierValueToken : null;
                for (int index = aggregate.TokenList.Count - 1; index >= 0; index--)
                {
                    Token child = aggregate.TokenList[index];
                    pending.Push((child, false, ReferenceEquals(child, modifierValue), 0, 0, -1,
                        capture && occurrences![occurrence].Transparent, aggregate, index));
                }
                continue;
            }
            else
            {
                builder.Append(current.ToString());
            }
            if (!close && occurrence >= 0)
            {
                SourceOccurrence original = occurrences![occurrence];
                occurrences[occurrence] = new(current, original.Start, offset + builder.Length,
                    occurrences.Count, original.Transparent, original.Slot);
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

    private static Token GetSourceToken(VariableRefToken reference,
        Dictionary<VariableRefToken, ReferenceSource>? sources)
    {
        ReferenceSource? source = reference is CustomReferenceOccurrence custom ? custom.Source :
            sources?.TryGetValue(reference, out var bound) == true &&
                (bound.Live || bound.Slot is { Removed: true } || bound.Slot?.Current is ResolvedJsonLiteral)
                    ? bound : null;
        return source is ReferenceSource location
            ? location.Slot is null ? location.Token : location.Slot.Current
            : reference;
    }

    private static string ResolveSourceToken(Token token, char escapeChar,
        IDictionary<string, string?>? variables, ResolutionOptions options) => token switch
    {
        LiteralToken literal => literal.ResolveVariablesInContext(false, escapeChar, variables, options) ?? string.Empty,
        AggregateToken aggregate => aggregate.ResolveVariables(escapeChar, variables, options) ?? string.Empty,
        _ => options.FormatValue(escapeChar, token.ToString())
    };

    private sealed class ResolvedJsonLiteral : LiteralToken
    {
        private readonly char sourceEscapeChar;
        private Expansion? pending;

        internal ResolvedJsonLiteral(Expansion value, char escapeChar)
            : base(Array.Empty<Token>(), canContainVariables: false, escapeChar)
        {
            pending = value;
            sourceEscapeChar = escapeChar;
        }

        internal override void MaterializeTokens()
        {
            if (pending is Expansion expansion)
            {
                string text = Encode(RenderExpansion(expansion, sourceEscapeChar));
                pending = null;
                ReplaceWithToken(new StringToken(text));
            }
        }

        protected override IEnumerable<Token> GetInnerTokens(string value)
        {
            IEnumerable<Token> tokens = base.GetInnerTokens(value);
            pending = null;
            return tokens;
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            options ??= new();
            if (pending is Expansion expansion && QuoteChar is null)
            {
                return options.FormatValue(escapeChar, expansion.Text ?? RenderExpansion(expansion, sourceEscapeChar));
            }
            string physical = "\"" + RenderToken(this) + "\"";
            List<int> offsets = new();
            string logical = ConstructReader.FoldHeader(physical, 0, escapeChar, out int end, offsets);
            if (end != physical.Length || !JsonStringValidation.IsValidLogical(logical))
            {
                return base.ResolveVariables(escapeChar, variables, options);
            }
            return options.FormatValue(escapeChar, Decode(logical, offsets, out _, out _));
        }
    }

    private sealed class CustomReferenceOccurrence(ReferenceSource source)
        : VariableRefToken(Array.Empty<Token>(), Dockerfile.DefaultEscapeChar)
    {
        internal ReferenceSource Source { get; } = source;

        protected override string GetUnderlyingValue(TokenStringOptions options) => RenderToken(Source.Token);

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null) =>
            ResolveSourceToken(GetSourceToken(this, null), escapeChar, variables, options ?? new());
    }

    internal readonly struct ReferenceSource(VariableRefToken token, bool live, SourceSlot? slot)
    {
        internal VariableRefToken Token { get; } = token;
        internal bool Live { get; } = live;
        internal SourceSlot? Slot { get; } = slot;
    }

    internal sealed class SourceSlot(AggregateToken parent, int index)
    {
        private static readonly Token removedToken = new StringToken("");

        internal AggregateToken Parent { get; } = parent;
        internal int Index { get; set; } = index;
        internal bool Removed { get; set; }
        internal Token Current => Removed ? removedToken : Parent.TokenList[Index];
    }

    internal sealed class SourceSlots : IDisposable
    {
        private readonly Dictionary<AggregateToken, ParentSlots> parents =
            new(ReferenceComparer<AggregateToken>.Instance);

        internal SourceSlot? Get(AggregateToken? parent, int index)
        {
            if (parent is null)
            {
                return null;
            }
            if (!parents.TryGetValue(parent, out var slots))
            {
                slots = new(parent);
                parents.Add(parent, slots);
            }
            if (!slots.Captured.TryGetValue(index, out var slot))
            {
                slot = new(parent, index);
                slots.Captured.Add(index, slot);
            }
            return slot;
        }

        internal int Remove(AggregateToken parent, int index) =>
            parents.TryGetValue(parent, out var slots) ? slots.Remove(index) : 0;

        public void Dispose()
        {
            foreach (ParentSlots slots in parents.Values)
            {
                slots.Dispose();
            }
        }

        private sealed class ParentSlots : IDisposable
        {
            private readonly AggregateToken parent;
            private List<SourceSlot>? active;
            internal Dictionary<int, SourceSlot> Captured { get; } = new();
            private List<SourceSlot> Active => active ??= Captured.Values
                .Where(slot => !slot.Removed).OrderBy(slot => slot.Index).ToList();

            internal ParentSlots(AggregateToken parent)
            {
                this.parent = parent;
                parent.ResolutionChildRemoved += OnRemove;
                parent.ResolutionChildrenReplaced += OnReplace;
            }

            private void OnRemove(int index) => Remove(index);

            internal int Remove(int index)
            {
                int low = 0;
                int high = Active.Count;
                while (low < high)
                {
                    int middle = low + (high - low) / 2;
                    if (Active[middle].Index < index)
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle;
                    }
                }
                int work = 0;
                if (low < Active.Count && Active[low].Index == index)
                {
                    Active[low].Removed = true;
                    Active.RemoveAt(low);
                    work++;
                }
                for (int current = low; current < Active.Count; current++)
                {
                    Active[current].Index--;
                    work++;
                }
                return work;
            }

            private void OnReplace(IReadOnlyList<Token> previous)
            {
                List<Token> current = parent.TokenList;
                int[] positions = new int[previous.Count];
                for (int index = 0; index < positions.Length; index++)
                {
                    positions[index] = -1;
                }
                if (previous.Count == current.Count)
                {
                    for (int index = 0; index < positions.Length; index++)
                    {
                        positions[index] = index;
                    }
                }
                else
                {
                    Dictionary<Token, Queue<int>> candidates = new(ReferenceComparer<Token>.Instance);
                    for (int index = 0; index < previous.Count; index++)
                    {
                        if (!candidates.TryGetValue(previous[index], out var indices))
                        {
                            indices = new();
                            candidates.Add(previous[index], indices);
                        }
                        indices.Enqueue(index);
                    }
                    int oldStart = 0;
                    int newStart = 0;
                    for (int index = 0; index <= current.Count; index++)
                    {
                        int next = previous.Count;
                        if (index < current.Count)
                        {
                            if (!candidates.TryGetValue(current[index], out var indices))
                            {
                                continue;
                            }
                            while (indices.Count > 0 && indices.Peek() < oldStart)
                            {
                                indices.Dequeue();
                            }
                            if (indices.Count == 0)
                            {
                                continue;
                            }
                            next = indices.Dequeue();
                        }
                        int replacements = Math.Min(next - oldStart, index - newStart);
                        for (int offset = 0; offset < replacements; offset++)
                        {
                            positions[oldStart + offset] = newStart + offset;
                        }
                        if (index < current.Count)
                        {
                            positions[next] = index;
                        }
                        oldStart = next + 1;
                        newStart = index + 1;
                    }
                }
                foreach (SourceSlot slot in Captured.Values)
                {
                    if (slot.Removed)
                    {
                        continue;
                    }
                    int replacement = positions[slot.Index];
                    if (replacement < 0)
                    {
                        slot.Removed = true;
                    }
                    else
                    {
                        slot.Index = replacement;
                    }
                }
                active = null;
            }

            public void Dispose()
            {
                parent.ResolutionChildRemoved -= OnRemove;
                parent.ResolutionChildrenReplaced -= OnReplace;
            }
        }
    }

    private readonly struct SourceOccurrence(Token token, int start, int end, int following, bool transparent,
        SourceSlot? slot = null)
    {
        internal Token Token { get; } = token;
        internal int Start { get; } = start;
        internal int End { get; } = end;
        internal int Following { get; } = following;
        internal bool Transparent { get; } = transparent;
        internal SourceSlot? Slot { get; } = slot;
    }

    private sealed class SourceSnapshot(string physical)
    {
        internal string Physical { get; } = physical;
        internal List<SourceOccurrence> Occurrences { get; } = new();
    }

    private static bool IsTransparentContainer(Token token) =>
        token is LiteralToken { CanContainVariables: true, HasCustomResolution: false, HasDefaultRendering: true } ||
        token is AggregateToken { HasDefaultAggregateRendering: true, HasCustomVariableResolution: false }
            and not Tokens.LiteralToken and not VariableRefToken;

    private static (Token Token, string Text, int Occurrence)[] ExposeReplacedDescendants(
        SourceSnapshot snapshot,
        IEnumerable<(int Start, int End, Token Token, bool Inline)> replacements,
        Dictionary<Token, (int Position, int Occurrence)>? emptyAnchors)
    {
        HashSet<int> anchors = new();
        foreach (var replacement in replacements)
        {
            if (emptyAnchors?.TryGetValue(replacement.Token, out var anchor) == true)
            {
                anchors.Add(anchor.Occurrence);
            }
        }
        List<SourceOccurrence> occurrences = snapshot.Occurrences;
        bool[] containsReplacement = new bool[occurrences.Count];
        var ranges = replacements.Where(replacement => replacement.Start < replacement.End).ToArray();
        int rangeIndex = 0;
        for (int index = 0; index < occurrences.Count; index++)
        {
            SourceOccurrence occurrence = occurrences[index];
            while (rangeIndex < ranges.Length && ranges[rangeIndex].End <= occurrence.Start)
            {
                rangeIndex++;
            }
            containsReplacement[index] =
                occurrence.Start < occurrence.End && rangeIndex < ranges.Length &&
                    ranges[rangeIndex].Start < occurrence.End ||
                occurrence.Start == occurrence.End && anchors.Contains(index);
        }
        for (int index = occurrences.Count - 1; index >= 0; index--)
        {
            if (!containsReplacement[index] && occurrences[index].Transparent)
            {
                for (int child = index + 1; child < occurrences[index].Following; child = occurrences[child].Following)
                {
                    if (containsReplacement[child])
                    {
                        containsReplacement[index] = true;
                        break;
                    }
                }
            }
        }
        List<(Token Token, string Text, int Occurrence)> expanded = new();
        for (int index = 0; index < occurrences.Count;)
        {
            SourceOccurrence occurrence = occurrences[index];
            if (containsReplacement[index] && occurrence.Transparent)
            {
                index++;
            }
            else
            {
                expanded.Add((occurrence.Slot?.Current ?? occurrence.Token,
                    snapshot.Physical.Substring(occurrence.Start, occurrence.End - occurrence.Start), index));
                index = occurrence.Following;
            }
        }
        return expanded.ToArray();
    }

    private static List<Token> BuildTokens((Token Token, string Text, int Occurrence)[] source,
        IEnumerable<(int Start, int End, Token Token, bool Inline)> replacements, int end,
        List<Token>? substitutions = null,
        Dictionary<Token, (int Position, int Occurrence)>? emptyAnchors = null,
        SourceSnapshot? snapshot = null)
    {
        if (snapshot is not null || substitutions is not null)
        {
            source = ExposeReplacedDescendants(
                snapshot ?? throw new InvalidOperationException("Inline reconstruction requires a source snapshot."),
                replacements, emptyAnchors);
        }
        List<Token> tokens = new();
        HashSet<int>? shiftedAnchors = null;
        foreach (var replacement in replacements)
        {
            if (emptyAnchors?.TryGetValue(replacement.Token, out var anchor) == true &&
                anchor.Position != replacement.Start)
            {
                shiftedAnchors ??= new();
                shiftedAnchors.Add(anchor.Occurrence);
            }
        }
        int tokenIndex = 0;
        int tokenStart = 1;
        int currentTokenIndex = -1;
        string currentText = "";
        void CopyUnchanged(int start, int stop, int anchor = -1)
        {
            while (tokenIndex < source.Length &&
                (tokenStart < stop || tokenStart == stop && source[tokenIndex].Text.Length == 0))
            {
                (Token token, string text, int occurrence) = source[tokenIndex];
                if (tokenStart == stop && anchor >= 0 && occurrence == anchor)
                {
                    break;
                }
                if (text.Length == 0 && shiftedAnchors?.Contains(occurrence) == true)
                {
                    tokenIndex++;
                    continue;
                }
                int tokenEnd = tokenStart + text.Length;
                int first = Math.Max(start - tokenStart, 0);
                int last = Math.Min(stop - tokenStart, text.Length);
                if (first < last || text.Length == 0 && tokenStart >= start)
                {
                    if (first == 0 && last == text.Length)
                    {
                        tokens.Add(token);
                    }
                    else
                    {
                        string current = text;
                        if (token is PrimitiveToken)
                        {
                            if (currentTokenIndex != tokenIndex)
                            {
                                currentText = token.ToString();
                                currentTokenIndex = tokenIndex;
                            }
                            current = currentText;
                        }
                        int currentFirst = Math.Min(first, current.Length);
                        int currentLast = last == text.Length ? current.Length : Math.Min(last, current.Length);
                        tokens.Add(new StringToken(current.Substring(currentFirst, currentLast - currentFirst)));
                    }
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
            int anchor = -1;
            if (emptyAnchors?.TryGetValue(substitution, out var occurrence) == true &&
                occurrence.Position == start)
            {
                anchor = occurrence.Occurrence;
            }
            CopyUnchanged(position, start, anchor);
            if (anchor >= 0 && tokenIndex < source.Length &&
                source[tokenIndex].Occurrence == anchor)
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
