using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>Edits a document's constructs while preserving parse boundaries and document-level formatting.</summary>
/// <param name="owner">The document whose raw item list backs this live view.</param>
/// <remarks>
/// Prospective edits validate staged token identity, directive placement, and incoming instruction
/// escape context before committing. Preserving removal can promote embedded comments or blank
/// lines to standalone items; clear instead requires a genuinely empty result.
/// </remarks>
internal sealed class DocumentListAdapter(Dockerfile owner) :
    IEditableListAdapter<DockerfileConstruct>, IEditableListClearAdapter
{
    public void ValidateWrite() => EditValidation.ValidateSupportedDocument(owner);
    public IReadOnlyList<DockerfileConstruct> Snapshot() => owner.RawItems.ToArray();
    public IEqualityComparer<DockerfileConstruct> Comparer => ReferenceComparer<DockerfileConstruct>.Instance;

    public void Insert(int index, DockerfileConstruct item)
    {
        ValidateIncoming(item);
        List<DockerfileConstruct> items = owner.RawItems.ToList();
        items.Insert(index, item);
        Apply(items, new TokenEditPlan(), new[] { item }, TriviaDisposition.Preserve);
    }

    public void Replace(int index, DockerfileConstruct item, TriviaDisposition trivia)
    {
        Guard.NotNull(item, nameof(item));
        DockerfileConstruct old = owner.RawItems[index];
        if (ReferenceEquals(old, item))
        {
            return;
        }
        ValidateIncoming(item);
        TokenEditPlan plan = new();
        List<DockerfileConstruct> items = owner.RawItems.ToList();
        items.RemoveAt(index);
        List<DockerfileConstruct> retained = new();
        if (trivia == TriviaDisposition.Preserve)
        {
            string oldText = old.ToString();
            string suffix = TrailingWhitespace(oldText);
            string newText = item.ToString();
            string replacementSuffix = TrailingWhitespace(newText);
            if (suffix.Length > 0 && replacementSuffix.Length > 0 && suffix != replacementSuffix)
            {
                throw new InvalidOperationException("The replacement has conflicting trailing trivia. Use Discard to select its formatting.");
            }
            if (suffix.Length > 0 && replacementSuffix.Length == 0)
            {
                plan.Edit(item).AddRange(WhitespaceTokens(suffix));
            }
            string indent = LeadingIndent(oldText);
            string replacementIndent = LeadingIndent(newText);
            if (indent.Length > 0 && replacementIndent.Length > 0 && indent != replacementIndent)
            {
                throw new InvalidOperationException("The replacement has conflicting indentation. Use Discard to select its formatting.");
            }
            if (indent.Length > 0 && replacementIndent.Length == 0)
            {
                plan.Edit(item).Insert(0, new WhitespaceToken(indent));
            }
            retained = RetainTrivia(old, includeBlankLines: true, blankLineLimit: oldText.Length - suffix.Length);
        }
        items.Insert(index, item);
        items.InsertRange(index + 1, retained);
        Apply(items, plan, new[] { item }, trivia);
    }

    public void Remove(IReadOnlyList<int> indices, TriviaDisposition trivia) =>
        RemoveCore(indices, trivia, requireEmpty: false);

    /// <remarks>
    /// Preserving clear rejects embedded trivia or a byte-order mark that would require
    /// leftover items. Discarding clear removes that formatting along with the selected payloads.
    /// </remarks>
    public void Clear(TriviaDisposition trivia) =>
        RemoveCore(Enumerable.Range(0, owner.RawItems.Count).ToArray(), trivia, requireEmpty: true);

    private void RemoveCore(IReadOnlyList<int> indices, TriviaDisposition trivia, bool requireEmpty)
    {
        EditValidation.ValidateSupportedDocument(owner);
        HashSet<int> selected = new(indices);
        List<DockerfileConstruct> items = new();
        for (int i = 0; i < owner.RawItems.Count; i++)
        {
            DockerfileConstruct item = owner.RawItems[i];
            if (!selected.Contains(i))
            {
                items.Add(item);
            }
            else if (trivia == TriviaDisposition.Preserve)
            {
                items.AddRange(RetainTrivia(item, includeBlankLines: true));
            }
        }
        if (requireEmpty && (items.Count != 0 ||
            trivia == TriviaDisposition.Preserve && owner.ToString().StartsWith("\uFEFF", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Clearing would discard embedded trivia. Use Clear(TriviaDisposition.Discard).");
        }
        Apply(items, new TokenEditPlan(), Array.Empty<DockerfileConstruct>(), trivia);
    }

    public void Move(int oldIndex, int newIndex)
    {
        List<DockerfileConstruct> items = owner.RawItems.ToList();
        DockerfileConstruct item = items[oldIndex];
        items.RemoveAt(oldIndex);
        items.Insert(newIndex, item);
        Apply(items, new TokenEditPlan(), Array.Empty<DockerfileConstruct>(), TriviaDisposition.Preserve);
    }

    private void ValidateIncoming(DockerfileConstruct item)
    {
        EditValidation.ValidateSupportedDocument(owner);
        Guard.NotNull(item, nameof(item));
        if (owner.RawItems.Any(existing => ReferenceEquals(existing, item)))
        {
            throw new InvalidOperationException("The construct already belongs to the document. Move it instead.");
        }
        EditValidation.ValidateTree(item);
    }

    /// <summary>Validates the complete staged document before publishing either token or item-list changes.</summary>
    /// <remarks>
    /// Recovery parsing permits existing opaque constructs, but the resulting construct kinds
    /// and boundaries must agree with the proposed list. Existing directive errors may survive;
    /// new errors or a body-affecting escape-directive change are rejected.
    /// </remarks>
    private void Apply(List<DockerfileConstruct> items, TokenEditPlan plan,
        IReadOnlyList<DockerfileConstruct> incoming, TriviaDisposition trivia)
    {
        EditValidation.ValidateSupportedDocument(owner);
        string before = owner.ToString();
        PreserveBom(items, plan, trivia);
        string tail = string.Empty;
        for (int i = 0; i < items.Count; i++)
        {
            string text = plan.Render(items[i]);
            if (i > 0 && items[i] is not Whitespace && HasContent(tail) && !StartsWithLineBreak(text))
            {
                string newLine = SeamNewLine(items, i, plan);
                plan.Edit(items[i - 1]).Add(new NewLineToken(newLine));
                tail = string.Empty;
            }
            int newline = text.LastIndexOf('\n');
            tail = newline >= 0 ? text.Substring(newline + 1) : tail + text;
        }

        ValidateIdentity(items, plan);
        string after = string.Concat(items.Select(plan.Render));
        var previousHeader = ReadHeader(before);
        var nextHeader = ReadHeader(after);
        List<string> remainingErrors = previousHeader.Errors.ToList();
        foreach (string error in nextHeader.Errors)
        {
            if (!remainingErrors.Remove(error))
            {
                throw new InvalidOperationException("The edit would introduce or change an invalid parser directive.");
            }
        }
        if (previousHeader.EscapeChar != nextHeader.EscapeChar &&
            items.OfType<Instruction>().Any())
        {
            throw new InvalidOperationException("Changing the escape directive requires reparsing the document body.");
        }
        foreach (Instruction instruction in incoming.OfType<Instruction>())
        {
            if (instruction.EditingEscapeChar != nextHeader.EscapeChar)
            {
                throw new InvalidOperationException("The instruction was constructed with a different escape character.");
            }
            EditValidation.ValidateEscapeContext(instruction, nextHeader.EscapeChar);
        }

        DockerfileParseResult parsed = Dockerfile.TryParse(after, new DockerfileParseOptions
        {
            Mode = DockerfileParseMode.Recover,
            UnknownInstructionBehavior = UnknownInstructionBehavior.Preserve
        });
        Dockerfile result = parsed.Dockerfile
            ?? throw new InvalidOperationException("The edited document could not be parsed.");
        if (result.ToString() != after ||
            !items.Where(item => item is not Whitespace).Select(Kind)
                .SequenceEqual(result.Items.Where(item => item is not Whitespace).Select(Kind)))
        {
            throw new InvalidOperationException("The edit would change construct boundaries or parser-directive placement.");
        }

        if (owner.RawItems.Capacity < items.Count)
        {
            owner.RawItems.Capacity = items.Count;
        }
        plan.Commit();
        owner.RawItems.Clear();
        owner.RawItems.AddRange(items);
    }

    private static string SeamNewLine(IReadOnlyList<DockerfileConstruct> items, int index, TokenEditPlan plan)
    {
        for (int distance = 0; distance < Math.Max(index, items.Count - index); distance++)
        {
            int previous = index - distance - 1;
            if (previous >= 0)
            {
                string? preceding = BoundaryNewLines(items[previous], plan).LastOrDefault();
                if (preceding is not null)
                {
                    return preceding;
                }
            }
            int next = index + distance;
            if (next < items.Count)
            {
                string? following = BoundaryNewLines(items[next], plan).FirstOrDefault();
                if (following is not null)
                {
                    return following;
                }
            }
        }
        return "\n";
    }

    private static IEnumerable<string> BoundaryNewLines(Token token, TokenEditPlan plan)
    {
        if (token is AggregateToken aggregate)
        {
            IEnumerable<Token> children = plan.Tokens(aggregate);
            if (token is HeredocBodyToken)
            {
                // Raw body line endings are payload, not formatting for document seams.
                children = children.SkipWhile(child => child is not HeredocDelimiterToken);
            }
            foreach (Token child in children)
            {
                foreach (string newline in BoundaryNewLines(child, plan))
                {
                    yield return newline;
                }
            }
        }
        else
        {
            string text = plan.Render(token);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    yield return i > 0 && text[i - 1] == '\r' ? "\r\n" : "\n";
                }
            }
        }
    }

    private void PreserveBom(List<DockerfileConstruct> items, TokenEditPlan plan, TriviaDisposition trivia)
    {
        if (owner.RawItems.Count == 0 || !owner.RawItems[0].ToString().StartsWith("\uFEFF", StringComparison.Ordinal))
        {
            return;
        }
        if (items.Count == 0)
        {
            if (trivia == TriviaDisposition.Discard)
            {
                return;
            }
            items.Add(new Whitespace(string.Empty));
        }
        if (ReferenceEquals(items[0], owner.RawItems[0]))
        {
            return;
        }

        List<Token> previous = plan.Edit(owner.RawItems[0]);
        if (previous.Count == 0 || previous[0].ToString() != "\uFEFF")
        {
            throw new InvalidOperationException("The BOM is not an independently editable token.");
        }
        if (plan.Render(items[0]).StartsWith("\uFEFF", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The replacement would introduce a second BOM.");
        }
        Token bom = previous[0];
        previous.RemoveAt(0);
        plan.Edit(items[0]).Insert(0, bom);
    }

    private static void ValidateIdentity(IEnumerable<DockerfileConstruct> items, TokenEditPlan plan)
    {
        HashSet<Token> seen = new(ReferenceComparer<Token>.Instance);
        foreach (DockerfileConstruct item in items)
        {
            Visit(item);
        }
        void Visit(Token token)
        {
            if (!seen.Add(token))
            {
                throw new InvalidOperationException("The edited document contains a cyclic or shared token.");
            }
            if (token is AggregateToken aggregate)
            {
                foreach (Token child in plan.Tokens(aggregate))
                {
                    Visit(child);
                }
            }
        }
    }

    private static List<DockerfileConstruct> RetainTrivia(DockerfileConstruct item, bool includeBlankLines,
        int blankLineLimit = int.MaxValue)
    {
        List<(int Offset, DockerfileConstruct Item)> retained = new();
        if (item is not Instruction)
        {
            return new List<DockerfileConstruct>();
        }
        string text = item.ToString();
        bool[] whitespace = new bool[text.Length];
        Visit(item, 0);
        if (includeBlankLines)
        {
            for (int start = 0; start < text.Length;)
            {
                int end = DirectiveHeader.LineEnd(text, start);
                if (end <= blankLineLimit && text[end - 1] == '\n' &&
                    Enumerable.Range(start, end - start).All(index => whitespace[index]))
                {
                    retained.Add((start, new Whitespace(text.Substring(start, end - start))));
                }
                start = end;
            }
        }
        return retained.OrderBy(entry => entry.Offset).Select(entry => entry.Item).ToList();

        void Visit(Token token, int offset)
        {
            if (token is HeredocBodyToken)
            {
                return;
            }
            if (token is CommentToken comment)
            {
                int lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
                string indent = text.Substring(lineStart, offset - lineStart);
                List<Token> tokens = new();
                if (indent.All(IsHorizontal))
                {
                    if (indent.Length > 0)
                    {
                        tokens.Add(new WhitespaceToken(indent));
                    }
                }
                tokens.Add(comment);
                retained.Add((offset, new Comment(tokens)));
                return;
            }
            if (token is WhitespaceToken)
            {
                for (int i = offset; i < offset + token.ToString().Length; i++)
                {
                    whitespace[i] = true;
                }
            }
            if (token is AggregateToken aggregate)
            {
                int current = offset + (token is VariableRefToken ? 1 : 0)
                    + (token is IQuotableToken quotable && quotable.QuoteChar.HasValue ? 1 : 0);
                foreach (Token child in aggregate.Tokens)
                {
                    Visit(child, current);
                    current += child.ToString().Length;
                }
            }
        }
    }

    private static (char EscapeChar, List<string> Errors) ReadHeader(string text)
    {
        DirectiveHeader header = new();
        List<string> errors = new();
        int start = text.StartsWith("\uFEFF", StringComparison.Ordinal) ? 1 : 0;
        while (start < text.Length && !header.Complete)
        {
            int end = DirectiveHeader.LineEnd(text, start);
            string line = text.Substring(start, end - start);
            header.Read(line, out string? error);
            if (error is not null)
            {
                errors.Add(error + "\0" + line.TrimEnd('\r', '\n'));
            }
            start = end;
        }
        return (header.EscapeChar, errors);
    }

    private static string Kind(DockerfileConstruct item) => item switch
    {
        ParserDirective directive => "directive:" + directive.DirectiveName.ToUpperInvariant(),
        UnknownInstruction instruction => "unknown:" + instruction.InstructionName.ToUpperInvariant(),
        Instruction instruction => "instruction:" + instruction.InstructionName.ToUpperInvariant(),
        _ => item.Type.ToString()
    };

    private static bool HasContent(string text) => text.Any(ch => !char.IsWhiteSpace(ch) && ch != '\uFEFF');

    private static bool StartsWithLineBreak(string text)
    {
        foreach (char ch in text)
        {
            if (ch == '\n')
            {
                return true;
            }
            if (!IsHorizontal(ch))
            {
                return false;
            }
        }
        return false;
    }

    private static bool IsHorizontal(char ch) => ch is ' ' or '\t' or '\r' or '\f';

    private static string TrailingWhitespace(string text)
    {
        int start = text.Length;
        while (start > 0 && char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }
        return text.Substring(start);
    }

    private static string LeadingIndent(string text)
    {
        int start = text.StartsWith("\uFEFF", StringComparison.Ordinal) ? 1 : 0;
        int end = start;
        while (end < text.Length && IsHorizontal(text[end]))
        {
            end++;
        }
        return end < text.Length && text[end] == '\n' ? string.Empty : text.Substring(start, end - start);
    }

    private static IEnumerable<Token> WhitespaceTokens(string text)
    {
        int start = 0;
        while (start < text.Length)
        {
            int end = text.IndexOf('\n', start);
            if (end < 0)
            {
                yield return new WhitespaceToken(text.Substring(start));
                yield break;
            }
            int contentEnd = end > start && text[end - 1] == '\r' ? end - 1 : end;
            if (contentEnd > start)
            {
                yield return new WhitespaceToken(text.Substring(start, contentEnd - start));
            }
            yield return new NewLineToken(text.Substring(contentEnd, end - contentEnd + 1));
            start = end + 1;
        }
    }
}
