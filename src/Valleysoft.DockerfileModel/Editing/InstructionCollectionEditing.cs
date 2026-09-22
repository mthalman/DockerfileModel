using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>Builds semantic projections over owner-bound token collections and supplies shared boundary helpers.</summary>
/// <remarks>
/// Projection factories require a <c>TokenList</c> backing instance even though their parameters
/// expose the common editable-list contract. Values use the owner's escape context;
/// JSON string values requiring escaping are rejected rather than implicitly encoded.
/// Projected replacement can retain the selected token rather than replacing its identity.
/// </remarks>
internal static class InstructionCollectionEditing
{
    /// <summary>Creates an ordinal string view that shares the literal list's syntax-aware edits.</summary>
    /// <param name="tokens">The owner's live literal token list, backed by a <c>TokenList</c>.</param>
    /// <param name="owner">The instruction or command determining operand grammar and escape context.</param>
    /// <returns>A synchronized semantic view; new strings are validated and quoted rather than adopted as syntax.</returns>
    /// <remarks>JSON values containing double quotes, backslashes, or U+0000–U+001F are unsupported.</remarks>
    public static EditableList<string> Strings(EditableList<LiteralToken> tokens, AggregateToken owner) =>
        new ProjectedItemList<LiteralToken, string>(tokens, token => token.Value,
            value => CreateLiteral(value, owner), StringComparer.Ordinal,
            (index, value, trivia) => ((TokenList<LiteralToken>)tokens).ReplaceValue(index, CreateLiteral(value, owner), trivia));

    /// <summary>Creates an ordinal pattern view over optional exclude flags.</summary>
    /// <param name="tokens">The owner's exclude flag list, backed by a <c>TokenList</c>.</param>
    /// <param name="owner">The file-transfer instruction supplying escape context and flag placement.</param>
    /// <returns>A synchronized view whose writes retain flags before positional operands.</returns>
    public static EditableList<string> Excludes(EditableList<ExcludeFlag> tokens, AggregateToken owner) =>
        new ProjectedItemList<ExcludeFlag, string>(tokens, token => token.Value,
            value => CreateExclude(value, owner), StringComparer.Ordinal,
            (index, value, trivia) => ((TokenList<ExcludeFlag>)tokens).ReplaceValue(index, CreateExclude(value, owner), trivia));

    private static ExcludeFlag CreateExclude(string value, AggregateToken owner)
    {
        Guard.NotNullOrEmpty(value, nameof(value));
        ExcludeFlag result = new(value, owner.EditingEscapeChar);
        if (result.Value != value)
        {
            throw new ArgumentException("The value must describe exactly one exclude pattern.", nameof(value));
        }
        return result;
    }

    /// <summary>Creates an assignment view with ordinal key/value equality rather than token identity equality.</summary>
    /// <typeparam name="T">The instruction-specific assignment token type.</typeparam>
    /// <param name="tokens">The owner's assignment list, backed by a <c>TokenList</c>.</param>
    /// <param name="owner">The instruction determining assignment syntax and allowed missing values.</param>
    /// <returns>A live view that encodes supplied pair data into validated assignment tokens.</returns>
    public static EditableList<IKeyValuePair> Pairs<T>(EditableList<T> tokens, Instruction owner)
        where T : Token, IKeyValuePair =>
        new ProjectedItemList<T, IKeyValuePair>(tokens, token => token,
            pair => CreatePair<T>(pair, owner), PairComparer.Instance,
            (index, pair, trivia) => ((TokenList<T>)tokens).ReplaceValue(index, CreatePair<T>(pair, owner), trivia));

    private static T CreatePair<T>(IKeyValuePair pair, Instruction owner) where T : Token, IKeyValuePair
    {
        Guard.NotNull(pair, "value");
        Guard.NotNullOrEmpty(pair.Key, nameof(pair));
        if (owner is not ArgInstruction && pair.Value is null)
        {
            throw new ArgumentException("This assignment requires a value.", nameof(pair));
        }
        string assignment = pair.Value is null
            ? pair.Key
            : pair.Value.Length == 0 ? $"{pair.Key}=\"\"" : StringHelper.FormatKeyValueAssignment(pair.Key, pair.Value);
        Instruction parsed = EditValidation.ParseInstruction(
            $"{owner.InstructionName} {assignment}", owner.EditingEscapeChar);
        T[] items = parsed.Tokens.OfType<T>().ToArray();
        if (items.Length != 1 || !PairComparer.Instance.Equals(items[0], pair))
        {
            throw new ArgumentException("The value must describe exactly one assignment.", nameof(pair));
        }
        return items[0];
    }

    private static LiteralToken CreateLiteral(string value, AggregateToken owner)
    {
        Guard.NotNull(value, nameof(value));
        if (owner is ExposeInstruction)
        {
            Guard.NotNullOrEmpty(value, nameof(value));
        }
        if (value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
        {
            throw new ArgumentException("An operand value must not contain physical newlines.", nameof(value));
        }
        if (owner is not ExecFormCommand && value.Length == 0)
        {
            throw new ArgumentException("This operand cannot be empty.", nameof(value));
        }
        bool variables = owner is not ExecFormCommand and not GenericInstruction;
        bool json = IsJson(owner);
        if (json && value.Any(character => character < ' ' || character is '\\' or '"'))
        {
            throw new ArgumentException("JSON operand values requiring escaping are not supported. Use a valid encoded token instead.", nameof(value));
        }
        LiteralToken token = new(value, variables, owner.EditingEscapeChar);
        if (token.Value != value)
        {
            throw new ArgumentException("The value must describe exactly one operand.", nameof(value));
        }
        if (json || (value.Any(char.IsWhiteSpace) && owner is not GenericInstruction))
        {
            token.QuoteChar = '"';
        }
        return token;
    }

    /// <summary>Detects JSON-form operands from direct punctuation without inspecting nested command text.</summary>
    /// <param name="owner">The live operand owner.</param>
    /// <returns>Whether its immediate tokens include the opening JSON array symbol.</returns>
    internal static bool IsJson(AggregateToken owner) =>
        owner.Tokens.OfType<SymbolToken>().Any(token => token.ToString() == "[");

    /// <summary>Infers a newline at an anchor boundary from the nearest line ending in the live serialization.</summary>
    /// <param name="owner">The live tree containing the anchor.</param>
    /// <param name="anchor">The exact token instance selecting the boundary.</param>
    /// <param name="after">Whether to measure from the end of the anchor rather than its start.</param>
    /// <returns>The nearest LF or CRLF sequence, preferring the preceding one on ties and LF when none exists.</returns>
    /// <remarks>Uses live, not staged, token positions so unrelated earlier lines do not dictate a new seam's style.</remarks>
    internal static string NewLineAt(AggregateToken owner, Token anchor, bool after)
    {
        string text = owner.ToString();
        int anchorOffset = FindOffset(owner, anchor, 0);
        if (anchorOffset < 0)
        {
            throw new ArgumentException("The anchor does not belong to the owner.", nameof(anchor));
        }
        int position = anchorOffset + (after ? anchor.ToString().Length : 0);
        int following = text.IndexOf('\n', Math.Min(position, text.Length));
        int preceding = position > 0 ? text.LastIndexOf('\n', position - 1) : -1;
        int nearest = preceding < 0 ? following :
            following < 0 || position - preceding <= following - position ? preceding : following;
        return nearest > 0 && text[nearest - 1] == '\r' ? "\r\n" : "\n";
    }

    private static int FindOffset(Token current, Token anchor, int offset)
    {
        if (ReferenceEquals(current, anchor))
        {
            return offset;
        }
        if (current is not AggregateToken aggregate)
        {
            return -1;
        }
        int childOffset = offset + (current is VariableRefToken ? 1 : 0) +
            (current is IQuotableToken { QuoteChar: not null } ? 1 : 0);
        foreach (Token child in aggregate.Tokens)
        {
            int found = FindOffset(child, anchor, childOffset);
            if (found >= 0)
            {
                return found;
            }
            childOffset += child.ToString().Length;
        }
        return -1;
    }

    /// <summary>Traverses a live token subtree in depth-first order, including its root.</summary>
    /// <param name="token">The root of a previously validated acyclic subtree.</param>
    /// <returns>The existing token instances, not clones or staged replacements.</returns>
    internal static IEnumerable<Token> Descendants(Token token)
    {
        yield return token;
        if (token is AggregateToken aggregate)
        {
            foreach (Token child in aggregate.Tokens)
            {
                foreach (Token descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>Compares projected assignment data ordinally, keeping missing and empty values distinct.</summary>
    internal sealed class PairComparer : IEqualityComparer<IKeyValuePair>
    {
        /// <summary>Gets the shared semantic assignment comparer.</summary>
        internal static PairComparer Instance { get; } = new();
        public bool Equals(IKeyValuePair? x, IKeyValuePair? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null &&
            StringComparer.Ordinal.Equals(x.Key, y.Key) && StringComparer.Ordinal.Equals(x.Value, y.Value));
        public int GetHashCode(IKeyValuePair obj) =>
            StringComparer.Ordinal.GetHashCode(obj.Key) ^ (obj.Value is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Value));
    }
}

/// <summary>Edits a filtered instruction or command token role using its existing shell or JSON syntax.</summary>
/// <typeparam name="T">The direct child token type exposed by the role.</typeparam>
/// <remarks>
/// Typed membership uses reference identity. Adoption checks nested escape context and
/// owner-local aliasing; writes stage separators, quoting, and payloads, then reparse and compare
/// operand counts and values before committing. JSON operands compare folded syntax rather than
/// legacy value projections; incoming operands must independently form one valid JSON string.
/// Required cardinality depends on the role.
/// </remarks>
internal sealed class InstructionTokenListAdapter<T> : IEditableListAdapter<T> where T : Token
{
    private readonly AggregateToken owner;
    private readonly Func<IEnumerable<T>, IEnumerable<T>>? filter;

    /// <summary>Binds a token role to its owner without copying its live payloads.</summary>
    /// <param name="owner">The instruction or command containing the selected direct children.</param>
    /// <param name="filter">
    /// An optional deterministic role selector applied to both live and reparsed tokens, such as
    /// excluding a file-transfer destination from the source view.
    /// </param>
    internal InstructionTokenListAdapter(AggregateToken owner, Func<IEnumerable<T>, IEnumerable<T>>? filter = null)
    {
        this.owner = owner;
        this.filter = filter;
    }

    private List<T> Select(AggregateToken aggregate) => Select(aggregate.Tokens);
    private List<T> Select(IEnumerable<Token> tokens)
    {
        IEnumerable<T> selected = tokens.OfType<T>();
        return (filter is null ? selected : filter(selected)).ToList();
    }

    public void ValidateWrite() => EditValidation.ValidateTree(owner);
    public IReadOnlyList<T> Snapshot() => Select(owner);
    public IEqualityComparer<T> Comparer => ReferenceComparer<T>.Instance;
    private bool IsFlag => typeof(T) == typeof(ExcludeFlag) || typeof(T) == typeof(MountFlag);
    private bool IsJson => !IsFlag && InstructionCollectionEditing.IsJson(owner);
    private int Minimum => IsFlag || owner is ExecFormCommand || owner is VolumeInstruction && IsJson ||
        owner is FileTransferInstruction transfer && transfer.HeredocMarkerTokens.Any() ? 0 : 1;

    public void Insert(int index, T item)
    {
        ValidateItem(item);
        TokenEditPlan plan = new();
        List<Token> tokens = plan.Edit(owner);
        List<T> expected = Select(tokens);
        NormalizeLegacyEnv(plan);
        PrepareItem(plan, item);
        Insert(tokens, expected, index, item);
        expected.Insert(index, item);
        ValidateAndCommit(plan, expected);
    }

    public void Replace(int index, T item, TriviaDisposition trivia)
    {
        List<T> expected = Select(owner);
        T current = expected[index];
        if (ReferenceEquals(current, item))
        {
            ValidateAndCommit(new TokenEditPlan(), expected);
            return;
        }
        ValidateItem(item);
        EnsureRemovable(current, trivia);
        TokenEditPlan plan = new();
        List<Token> tokens = plan.Edit(owner);
        PrepareItem(plan, item);
        tokens[ReferenceList.IndexOf(tokens, current)] = item;
        expected[index] = item;
        ValidateAndCommit(plan, expected);
    }

    public void Remove(IReadOnlyList<int> indices, TriviaDisposition trivia)
    {
        List<T> expected = Select(owner);
        if (expected.Count - indices.Count < Minimum)
        {
            throw new InvalidOperationException("This collection requires at least one operand.");
        }

        foreach (int index in indices)
        {
            EnsureRemovable(expected[index], trivia);
        }
        TokenEditPlan plan = new();
        List<Token> tokens = plan.Edit(owner);
        foreach (int index in indices.OrderByDescending(index => index))
        {
            Remove(tokens, expected[index], trivia);
            expected.RemoveAt(index);
        }
        ValidateAndCommit(plan, expected);
    }

    /// <summary>Applies an encoded semantic replacement while retaining the selected aggregate when possible.</summary>
    /// <param name="index">The selected element in the current role view.</param>
    /// <param name="replacement">A detached token encoding the desired value in the owner's context.</param>
    /// <param name="trivia">Whether compatible existing separators and quotes must be preserved.</param>
    /// <remarks>
    /// Unlike typed replacement, this path may transfer replacement children into the existing
    /// token. It still rejects unpreservable embedded trivia and validates the complete prospective owner.
    /// </remarks>
    internal void ReplaceValue(int index, T replacement, TriviaDisposition trivia)
    {
        List<T> expected = Select(owner);
        T current = expected[index];
        if (current is not AggregateToken aggregate || replacement is not AggregateToken replacementAggregate)
        {
            Replace(index, replacement, trivia);
            return;
        }
        ValidateItem(replacement);
        EnsureRemovable(current, trivia);
        TokenEditPlan plan = new();
        List<Token> children = plan.Edit(aggregate);
        Token[] oldOperands = children.Where(token => token is AggregateToken && token is not LineContinuationToken).ToArray();
        Token[] newOperands = replacementAggregate.Tokens.Where(token => token is AggregateToken &&
            token is not LineContinuationToken).ToArray();
        if (trivia == TriviaDisposition.Preserve && current is IKeyValuePair &&
            oldOperands.Length == newOperands.Length && oldOperands.Length > 0)
        {
            for (int operand = 0; operand < oldOperands.Length; operand++)
            {
                children[ReferenceList.IndexOf(children, oldOperands[operand])] = newOperands[operand];
            }
        }
        else
        {
            children.Clear();
            children.AddRange(replacementAggregate.Tokens);
        }
        if (current is IQuotableToken currentQuote && replacement is IQuotableToken newQuote)
        {
            plan.SetQuote(currentQuote, IsJson ? '"' :
                trivia == TriviaDisposition.Preserve ? currentQuote.QuoteChar ?? newQuote.QuoteChar : newQuote.QuoteChar);
        }
        expected[index] = replacement;
        ValidateAndCommit(plan, expected);
    }

    public void Move(int oldIndex, int newIndex)
    {
        TokenEditPlan plan = new();
        List<Token> tokens = plan.Edit(owner);
        List<T> original = Select(tokens);
        List<T> expected = original.ToList();
        T item = expected[oldIndex];
        expected.RemoveAt(oldIndex);
        expected.Insert(newIndex, item);
        for (int index = 0; index < original.Count; index++)
        {
            tokens[ReferenceList.IndexOf(owner.TokenList, original[index])] = expected[index];
        }
        ValidateAndCommit(plan, expected);
    }

    private void ValidateItem(T item)
    {
        Guard.NotNull(item, nameof(item));
        if (owner is UnknownInstruction)
        {
            throw new InvalidOperationException("Unknown instruction arguments are opaque.");
        }
        if (item is AggregateToken aggregate && aggregate.EditingEscapeChar != owner.EditingEscapeChar)
        {
            throw new InvalidOperationException("The operand uses an incompatible escape character.");
        }
        EditValidation.ValidateTree(owner);
        EditValidation.ValidateTree(item);
        EditValidation.ValidateEscapeContext(item, owner.EditingEscapeChar);
        Token[] incoming = InstructionCollectionEditing.Descendants(item).ToArray();
        HashSet<Token> existing = new(InstructionCollectionEditing.Descendants(owner), ReferenceComparer<Token>.Instance);
        if (incoming.Any(existing.Contains))
        {
            throw new InvalidOperationException("The token already belongs to this owner.");
        }
        if (IsJson && item is IQuotableToken quotable)
        {
            TokenEditPlan prospective = new();
            prospective.SetQuote(quotable, '"');
            if (!JsonStringValidation.IsValid(prospective.Render(item), owner.EditingEscapeChar))
            {
                throw new InvalidOperationException("The operand must represent exactly one valid JSON string.");
            }
        }
    }

    private static void EnsureRemovable(T token, TriviaDisposition trivia)
    {
        if (trivia == TriviaDisposition.Preserve &&
            InstructionCollectionEditing.Descendants(token).Any(item => item is CommentToken ||
                item is NewLineToken && token is not CommentToken))
        {
            throw new InvalidOperationException("Nested comments or line trivia cannot be safely detached from this operand.");
        }
    }

    private void PrepareItem(TokenEditPlan plan, T item)
    {
        if (IsJson && item is IQuotableToken quotable)
        {
            plan.SetQuote(quotable, '"');
        }
    }

    private void Insert(List<Token> tokens, List<T> selected, int index, T item)
    {
        if (index < selected.Count)
        {
            int position = ReferenceList.IndexOf(tokens, selected[index]);
            tokens.InsertRange(position, new Token[] { item }.Concat(Separator(selected[index], after: false)));
            return;
        }
        if (selected.Count > 0)
        {
            int position = ReferenceList.IndexOf(tokens, selected[selected.Count - 1]) + 1;
            tokens.InsertRange(position, Separator(selected[selected.Count - 1], after: true).Concat(new Token[] { item }));
            return;
        }
        if (IsFlag)
        {
            int position = tokens.FindIndex(token => token is LiteralToken or Command or HeredocMarkerToken ||
                token is SymbolToken && token.ToString() == "[");
            if (position < 0)
            {
                throw new InvalidOperationException("Cannot locate an operand boundary for the flag.");
            }
            tokens.InsertRange(position, new Token[] { item, new WhitespaceToken(" ") });
        }
        else if (IsJson)
        {
            int position = tokens.FindIndex(token => token is SymbolToken && token.ToString() == "[") + 1;
            Token? following = tokens.Skip(position).FirstOrDefault(token => token is LiteralToken);
            tokens.Insert(position, item);
            if (following is not null)
            {
                tokens.InsertRange(position + 1, Separator(following, after: false));
            }
        }
        else if (owner is FileTransferInstruction transfer && transfer.DestinationToken is { } destination)
        {
            tokens.InsertRange(ReferenceList.IndexOf(tokens, destination), new Token[] { item, new WhitespaceToken(" ") });
        }
        else
        {
            int position = tokens.FindIndex(token => token is KeywordToken) + 1;
            tokens.InsertRange(position, new Token[] { new WhitespaceToken(" "), item });
        }
    }

    private IEnumerable<Token> Separator(Token anchor, bool after)
    {
        if (IsJson)
        {
            yield return new SymbolToken(',');
        }
        yield return new WhitespaceToken(" ");
        if (owner is GenericInstruction)
        {
            yield return new LineContinuationToken(InstructionCollectionEditing.NewLineAt(owner, anchor, after), owner.EditingEscapeChar);
        }
    }

    private void Remove(List<Token> tokens, T item, TriviaDisposition trivia)
    {
        int position = ReferenceList.IndexOf(tokens, item);
        tokens.RemoveAt(position);
        if (owner is GenericInstruction)
        {
            int following = position;
            while (following < tokens.Count && tokens[following] is WhitespaceToken)
            {
                following++;
            }
            if (following < tokens.Count && tokens[following] is LineContinuationToken)
            {
                tokens.RemoveAt(following);
            }
            else
            {
                int previous = position - 1;
                while (previous >= 0 && tokens[previous] is WhitespaceToken)
                {
                    previous--;
                }
                if (previous >= 0 && tokens[previous] is LineContinuationToken)
                {
                    tokens.RemoveAt(previous);
                    position--;
                }
            }
        }
        if (IsJson)
        {
            int comma = position;
            while (comma < tokens.Count && IsTrivia(tokens[comma]))
            {
                comma++;
            }
            if (comma >= tokens.Count || tokens[comma].ToString() != ",")
            {
                comma = position - 1;
                while (comma >= 0 && IsTrivia(tokens[comma]))
                {
                    comma--;
                }
            }
            if (comma >= 0 && comma < tokens.Count && tokens[comma] is SymbolToken && tokens[comma].ToString() == ",")
            {
                tokens.RemoveAt(comma);
                if (comma < position)
                {
                    position--;
                }
            }
        }
        if (trivia == TriviaDisposition.Discard)
        {
            while (position < tokens.Count && IsTrivia(tokens[position]) && tokens[position] is not NewLineToken)
            {
                tokens.RemoveAt(position);
            }
        }
        else if (position < tokens.Count && tokens[position] is WhitespaceToken whitespace &&
            !whitespace.ToString().Contains('\n'))
        {
            tokens.RemoveAt(position);
        }
    }

    private static bool IsTrivia(Token token) =>
        token is WhitespaceToken or NewLineToken or LineContinuationToken or CommentToken;

    /// <summary>Stages legacy ENV conversion so appending an assignment cannot absorb it into the old value.</summary>
    /// <remarks>
    /// Existing value meaning is retained, including moving terminal newline tokens outside
    /// the value before adding quotes. Ambiguous separator trivia is rejected rather than reconstructed.
    /// </remarks>
    private void NormalizeLegacyEnv(TokenEditPlan plan)
    {
        if (owner is not EnvInstruction)
        {
            return;
        }
        foreach (KeyValueToken<Variable, LiteralToken> pair in owner.Tokens.OfType<KeyValueToken<Variable, LiteralToken>>())
        {
            if (pair.Tokens.OfType<SymbolToken>().Any(token => token.ToString() == "="))
            {
                continue;
            }
            List<Token> tokens = plan.Edit(pair);
            int key = ReferenceList.IndexOf(tokens, pair.KeyToken);
            LiteralToken? valueToken = pair.ValueToken;
            int value = valueToken is null ? -1 : ReferenceList.IndexOf(tokens, valueToken);
            if (value <= key || tokens.Skip(key + 1).Take(value - key - 1).Any(token => token is not WhitespaceToken))
            {
                throw new InvalidOperationException("Legacy ENV trivia cannot be preserved during conversion.");
            }
            tokens.RemoveRange(key + 1, value - key - 1);
            tokens.Insert(key + 1, new SymbolToken('='));
            List<Token> valueChildren = plan.Edit(valueToken!);
            List<Token> lineEndings = new();
            while (valueChildren.Count > 0 && valueChildren[valueChildren.Count - 1] is NewLineToken)
            {
                lineEndings.Insert(0, valueChildren[valueChildren.Count - 1]);
                valueChildren.RemoveAt(valueChildren.Count - 1);
            }
            if (lineEndings.Count > 0)
            {
                List<Token> instructionTokens = plan.Edit(owner);
                instructionTokens.InsertRange(ReferenceList.IndexOf(instructionTokens, pair) + 1, lineEndings);
            }
            if (pair.Value.Any(char.IsWhiteSpace))
            {
                plan.SetQuote(valueToken!, '"');
            }
        }
    }

    /// <summary>Requires complete grammar consumption and the expected role values before publishing a plan.</summary>
    /// <remarks>Generic argument-line edits also validate the underlying known instruction grammar.</remarks>
    private void ValidateAndCommit(TokenEditPlan plan, List<T> expected)
    {
        EditValidation.ValidateTree(owner);
        string text = plan.Render(owner);
        AggregateToken parsed;
        try
        {
            parsed = owner switch
            {
                ExecFormCommand => ExecFormCommand.Parse(text, owner.EditingEscapeChar),
                UnknownInstruction => throw new InvalidOperationException("Unknown instruction arguments are opaque."),
                GenericInstruction => GenericInstruction.Parse(text, owner.EditingEscapeChar),
                Instruction => EditValidation.ParseInstruction(text, owner.EditingEscapeChar),
                _ => throw new InvalidOperationException("This owner does not define an editable instruction collection.")
            };
        }
        catch (Valleysoft.DockerfileModel.Parsing.ParseException exception)
        {
            throw new InvalidOperationException("The edit would produce invalid collection syntax.", exception);
        }
        if (parsed.ToString() != text)
        {
            throw new InvalidOperationException("The edited collection was not consumed completely.");
        }
        if (owner is GenericInstruction)
        {
            EditValidation.ParseInstruction(owner.ToString(), owner.EditingEscapeChar);
            Instruction diagnostic = EditValidation.ParseInstruction(text, owner.EditingEscapeChar);
            EditValidation.ValidateHeredocAssociations(owner, diagnostic, plan);
        }
        else if (parsed is Instruction instruction)
        {
            EditValidation.ValidateHeredocAssociations(owner, instruction, plan);
        }
        List<T> actual = Select(parsed);
        if (actual.Count != expected.Count || actual.Where((token, index) => !SameValue(token, expected[index], plan)).Any())
        {
            throw new InvalidOperationException("The edit changes operand boundaries or values.");
        }
        plan.Commit();
    }

    private bool SameValue(T left, T right, TokenEditPlan plan)
    {
        if (IsJson && left is LiteralToken && right is LiteralToken)
        {
            return ConstructReader.FoldHeader(left.ToString(), 0, owner.EditingEscapeChar, out _) ==
                ConstructReader.FoldHeader(plan.Render(right), 0, owner.EditingEscapeChar, out _);
        }
        return (left, right) switch
        {
            (IKeyValuePair first, IKeyValuePair second) => InstructionCollectionEditing.PairComparer.Instance.Equals(first, second),
            (IValueToken first, IValueToken second) => first.Value == second.Value,
            _ => left.ToString() == right.ToString()
        };
    }
}
