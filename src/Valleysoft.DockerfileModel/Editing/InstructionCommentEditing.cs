using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>Creates synchronized comment-token and comment-text views over an instruction's nested syntax.</summary>
internal static class InstructionCommentEditing
{
    /// <summary>Creates a live, depth-first comment view with reference-based membership.</summary>
    /// <param name="owner">The aggregate to inspect; structural writes require a supported instruction owner.</param>
    /// <returns>A view whose typed writes adopt comment tokens and validate the enclosing instruction.</returns>
    internal static EditableList<CommentToken> Tokens(AggregateToken owner) =>
        new(new CommentListAdapter(owner));

    /// <summary>Creates a semantic comment view sharing the token adapter's ordering and boundary rules.</summary>
    /// <param name="owner">The aggregate to inspect; structural writes require a supported instruction owner.</param>
    /// <returns>A live text projection whose replacements retain the selected comment token.</returns>
    internal static EditableList<string?> Values(AggregateToken owner)
    {
        CommentListAdapter adapter = new(owner);
        EditableList<CommentToken> tokens = new(adapter);
        return new ProjectedItemList<CommentToken, string?>(tokens, token => token.Text, Create,
            replace: adapter.ReplaceText);
    }

    /// <summary>Encodes exactly one comment payload without adding a line ending.</summary>
    /// <param name="text">The comment text; null or empty creates a bare comment marker.</param>
    /// <returns>A detached comment token ready for boundary-aware insertion.</returns>
    /// <remarks>Physical newlines or text that cannot round-trip as one payload are rejected.</remarks>
    internal static CommentToken Create(string? text)
    {
        if (text?.IndexOfAny(new[] { '\r', '\n' }) >= 0)
        {
            throw new ArgumentException("A comment must not contain a physical newline.", nameof(text));
        }
        CommentToken result = text is null || text.Length == 0
            ? new CommentToken(new Token[] { new SymbolToken('#') })
            : new CommentToken(text!);
        if (!string.IsNullOrEmpty(text) && result.Text != text)
        {
            throw new ArgumentException("The value must round-trip as one comment payload.", nameof(text));
        }
        return result;
    }

    /// <summary>Inserts a comment at an owned semantic-token boundary rather than a comment-list index.</summary>
    /// <param name="owner">The instruction to edit.</param>
    /// <param name="anchor">An existing comment or direct semantic token identifying the boundary.</param>
    /// <param name="text">The single-line comment payload.</param>
    /// <param name="after">Whether to insert after the anchor instead of before it.</param>
    internal static void InsertAnchored(Instruction owner, Token anchor, string text, bool after)
    {
        Guard.NotNull(anchor, nameof(anchor));
        Guard.NotNull(text, nameof(text));
        new CommentListAdapter(owner).InsertAnchored(anchor, Create(text), after);
    }
}

/// <summary>Edits depth-first comments without changing the enclosing instruction's operand text.</summary>
/// <remarks>
/// Comment payloads are context-independent, while new continuations use the owner's escape
/// character and the nearby boundary's newline style. Candidate edits are reparsed before commit;
/// unsafe inline/continued transitions and unknown instruction syntax are rejected unchanged.
/// </remarks>
internal sealed class CommentListAdapter : IEditableListAdapter<CommentToken>
{
    private readonly AggregateToken owner;
    /// <summary>Binds comment traversal and edit validation to one live owner tree.</summary>
    /// <param name="owner">The aggregate to inspect; supported known instructions may also be edited.</param>
    internal CommentListAdapter(AggregateToken owner) => this.owner = owner;
    public void ValidateWrite() => EditValidation.ValidateTree(owner);
    public IEqualityComparer<CommentToken> Comparer => ReferenceComparer<CommentToken>.Instance;
    public IReadOnlyList<CommentToken> Snapshot() => Locations(forEditing: false).Select(location => location.Comment).ToArray();

    private List<(AggregateToken Parent, CommentToken Comment)> Locations(bool forEditing = true)
    {
        EditValidation.ValidateTree(owner, forEditing);
        List<(AggregateToken, CommentToken)> result = new();
        Visit(owner);
        return result;

        void Visit(AggregateToken parent)
        {
            foreach (Token token in parent.Tokens)
            {
                if (token is CommentToken comment)
                {
                    result.Add((parent, comment));
                }
                else if (token is AggregateToken aggregate)
                {
                    Visit(aggregate);
                }
            }
        }
    }

    private void ValidateItem(CommentToken item)
    {
        Guard.NotNull(item, nameof(item));
        EditValidation.ValidateTree(item);
        HashSet<Token> existing = new(InstructionCollectionEditing.Descendants(owner), ReferenceComparer<Token>.Instance);
        if (InstructionCollectionEditing.Descendants(item).Any(existing.Contains))
        {
            throw new InvalidOperationException("The comment already belongs to this instruction.");
        }
        string text = item.ToString().TrimEnd('\r', '\n');
        if (!text.StartsWith("#", StringComparison.Ordinal) || text.IndexOfAny(new[] { '\r', '\n' }) >= 0)
        {
            throw new ArgumentException("A comment must contain exactly one comment line.", nameof(item));
        }
    }

    /// <remarks>
    /// With no existing comments, insertion uses the header continuation boundary. Otherwise
    /// insertion respects depth-first order; appending after a trailing inline comment is rejected
    /// when no legal continuation boundary exists.
    /// </remarks>
    public void Insert(int index, CommentToken item)
    {
        ValidateItem(item);
        List<(AggregateToken Parent, CommentToken Comment)> locations = Locations();
        TokenEditPlan plan = new();
        List<CommentToken> expected = locations.Select(location => location.Comment).ToList();
        if (index < locations.Count)
        {
            var target = locations[index];
            EnsureNewLine(plan, item, InstructionCollectionEditing.NewLineAt(owner, target.Comment, after: false));
            List<Token> tokens = plan.Edit(target.Parent);
            tokens.Insert(ReferenceList.IndexOf(tokens, target.Comment), item);
        }
        else if (locations.Count > 0)
        {
            var target = locations[locations.Count - 1];
            EnsureNewLine(plan, item, InstructionCollectionEditing.NewLineAt(owner, target.Comment, after: true));
            if (!target.Comment.ToString().EndsWith("\n", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A trailing inline comment has no continuation boundary for another comment.");
            }
            List<Token> tokens = plan.Edit(target.Parent);
            tokens.Insert(ReferenceList.IndexOf(tokens, target.Comment) + 1, item);
        }
        else
        {
            List<Token> tokens = plan.Edit(owner);
            int keyword = tokens.FindIndex(token => token is KeywordToken);
            if (keyword < 0)
            {
                throw new InvalidOperationException("This owner has no instruction header continuation boundary.");
            }
            int position = keyword + 1;
            while (position < tokens.Count && tokens[position] is WhitespaceToken)
            {
                position++;
            }
            string newline = InstructionCollectionEditing.NewLineAt(owner, tokens[keyword], after: true);
            EnsureNewLine(plan, item, newline);
            if (position < tokens.Count && tokens[position] is LineContinuationToken)
            {
                tokens.Insert(position + 1, item);
            }
            else
            {
                tokens.InsertRange(position, new Token[]
                {
                    new LineContinuationToken(newline, owner.EditingEscapeChar), item
                });
            }
        }
        expected.Insert(index, item);
        ValidateAndCommit(plan, expected);
    }

    /// <summary>Stages a comment at a semantic boundary, reusing an existing continuation where possible.</summary>
    /// <param name="anchor">An owned comment or direct non-trivia instruction token.</param>
    /// <param name="item">The detached single-line comment token to adopt.</param>
    /// <param name="after">Whether to select the boundary after the anchor rather than before it.</param>
    /// <remarks>Boundary repair and newline inference are validated as part of the complete instruction.</remarks>
    public void InsertAnchored(Token anchor, CommentToken item, bool after)
    {
        EditValidation.ValidateTree(owner);
        ValidateItem(item);
        if (anchor is CommentToken existing)
        {
            int index = ReferenceList.IndexOf(Snapshot(), existing);
            if (index < 0)
            {
                throw new ArgumentException("The anchor is not part of this instruction.", nameof(anchor));
            }
            Insert(index + (after ? 1 : 0), item);
            return;
        }
        if (anchor is WhitespaceToken or NewLineToken or LineContinuationToken ||
            !ReferenceList.Contains(owner.Tokens, anchor))
        {
            throw new ArgumentException("The anchor must be a direct semantic instruction token.", nameof(anchor));
        }
        TokenEditPlan plan = new();
        List<Token> tokens = plan.Edit(owner);
        int position = ReferenceList.IndexOf(tokens, anchor) + (after ? 1 : 0);
        if (after && position >= tokens.Count || !after && anchor is KeywordToken)
        {
            throw new InvalidOperationException("The anchor does not have a following instruction continuation boundary.");
        }
        string newline = InstructionCollectionEditing.NewLineAt(owner, anchor, after);
        int boundary = position;
        if (after)
        {
            while (boundary < tokens.Count && tokens[boundary] is WhitespaceToken)
            {
                boundary++;
            }
            if (boundary < tokens.Count && tokens[boundary] is LineContinuationToken)
            {
                position = boundary + 1;
            }
            else
            {
                boundary = -1;
            }
        }
        else
        {
            boundary--;
            while (boundary >= 0 && tokens[boundary] is WhitespaceToken)
            {
                boundary--;
            }
            if (boundary < 0 || tokens[boundary] is not (LineContinuationToken or CommentToken) ||
                !tokens[boundary].ToString().EndsWith("\n", StringComparison.Ordinal))
            {
                boundary = -1;
            }
        }
        if (boundary >= 0)
        {
            newline = InstructionCollectionEditing.NewLineAt(owner, tokens[boundary], after: true);
            EnsureNewLine(plan, item, newline);
            tokens.Insert(position, item);
        }
        else
        {
            EnsureNewLine(plan, item, newline);
            tokens.InsertRange(position, new Token[]
            {
                new WhitespaceToken(" "),
                new LineContinuationToken(newline, owner.EditingEscapeChar),
                item
            });
        }
        ValidateAndCommit(plan, null);
    }

    public void Replace(int index, CommentToken item, TriviaDisposition trivia)
    {
        var target = Locations()[index];
        if (ReferenceEquals(item, target.Comment))
        {
            ValidateAndCommit(new TokenEditPlan(), Snapshot().ToList());
            return;
        }
        ValidateItem(item);
        TokenEditPlan plan = new();
        if (target.Comment.ToString().EndsWith("\n", StringComparison.Ordinal))
        {
            EnsureNewLine(plan, item, InstructionCollectionEditing.NewLineAt(owner, target.Comment, after: true));
        }
        if (trivia == TriviaDisposition.Preserve)
        {
            List<Token> replacement = plan.Edit(item);
            Token[] prefix = target.Comment.Tokens.Skip(1).TakeWhile(token => token is WhitespaceToken).ToArray();
            if (prefix.Length > 0 && replacement.Count > 1 && replacement[1] is not WhitespaceToken)
            {
                replacement.InsertRange(1, prefix);
            }
        }
        List<Token> tokens = plan.Edit(target.Parent);
        tokens[ReferenceList.IndexOf(tokens, target.Comment)] = item;
        List<CommentToken> expected = Snapshot().ToList();
        expected[index] = item;
        ValidateAndCommit(plan, expected);
    }

    /// <remarks>
    /// Selected comments, including their own line endings, are payload. Surrounding continuation
    /// tokens remain in place, and all selected comments are removed in one validated plan.
    /// </remarks>
    public void Remove(IReadOnlyList<int> indices, TriviaDisposition trivia)
    {
        List<(AggregateToken Parent, CommentToken Comment)> locations = Locations();
        TokenEditPlan plan = new();
        foreach (int index in indices)
        {
            var target = locations[index];
            List<Token> tokens = plan.Edit(target.Parent);
            ReferenceList.Remove(tokens, target.Comment);
        }

        HashSet<int> removed = new(indices);
        ValidateAndCommit(plan, locations.Where((_, index) => !removed.Contains(index)).Select(location => location.Comment).ToList());
    }

    /// <summary>Replaces a comment's text through staged children while retaining the comment object.</summary>
    /// <param name="index">The selected comment's depth-first position.</param>
    /// <param name="text">The replacement payload; null or empty removes the text but retains the marker.</param>
    /// <param name="trivia">Whether existing non-payload formatting inside the comment must be retained.</param>
    internal void ReplaceText(int index, string? text, TriviaDisposition trivia)
    {
        var target = Locations()[index];
        CommentToken replacement = InstructionCommentEditing.Create(text);
        TokenEditPlan plan = new();
        List<Token> children = plan.Edit(target.Comment);
        if (trivia == TriviaDisposition.Discard)
        {
            children.Clear();
            children.AddRange(replacement.Tokens);
            if (target.Comment.ToString().EndsWith("\n", StringComparison.Ordinal))
            {
                children.Add(new NewLineToken(InstructionCollectionEditing.NewLineAt(owner, target.Comment, after: true)));
            }
        }
        else
        {
            StringToken? existing = target.Comment.TextToken;
            int position = existing is null
                ? children.FindIndex(token => token is NewLineToken)
                : ReferenceList.IndexOf(children, existing);
            if (position < 0)
            {
                position = children.Count;
            }
            if (existing is not null)
            {
                children.RemoveAt(position);
            }
            if (replacement.TextToken is { } payload)
            {
                children.Insert(position, payload);
            }
        }
        List<CommentToken> expected = Snapshot().ToList();
        expected[index] = replacement;
        ValidateAndCommit(plan, expected);
    }

    /// <remarks>Inline comments and newline-terminated comments cannot exchange incompatible boundary positions.</remarks>
    public void Move(int oldIndex, int newIndex)
    {
        List<(AggregateToken Parent, CommentToken Comment)> locations = Locations();
        List<CommentToken> expected = locations.Select(location => location.Comment).ToList();
        CommentToken moved = expected[oldIndex];
        expected.RemoveAt(oldIndex);
        expected.Insert(newIndex, moved);
        TokenEditPlan plan = new();
        for (int index = 0; index < locations.Count; index++)
        {
            var target = locations[index];
            if (target.Comment.ToString().EndsWith("\n", StringComparison.Ordinal) !=
                expected[index].ToString().EndsWith("\n", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Inline and continued comments cannot exchange positions safely.");
            }
            List<Token> tokens = plan.Edit(target.Parent);
            tokens[ReferenceList.IndexOf(target.Parent.TokenList, target.Comment)] = expected[index];
        }
        ValidateAndCommit(plan, expected);
    }

    private static void EnsureNewLine(TokenEditPlan plan, CommentToken item, string newline)
    {
        if (!item.ToString().EndsWith("\n", StringComparison.Ordinal))
        {
            plan.Edit(item).Add(new NewLineToken(newline));
        }
    }

    /// <summary>Checks complete serialization, unchanged operand text and roles, and optional expected comment order before commit.</summary>
    /// <remarks>Null and empty comment text are equivalent bare-marker payloads for this validation.</remarks>
    private void ValidateAndCommit(TokenEditPlan plan, List<CommentToken>? expected)
    {
        string text = plan.Render(owner);
        AggregateToken parsed = owner switch
        {
            UnknownInstruction => throw new InvalidOperationException("Unknown instruction arguments are opaque."),
            GenericInstruction => GenericInstruction.Parse(text, owner.EditingEscapeChar),
            Instruction => EditValidation.ParseInstruction(text, owner.EditingEscapeChar),
            _ => throw new InvalidOperationException("Comments require a known instruction owner.")
        };
        if (text != parsed.ToString() || SemanticText(owner) != SemanticText(parsed))
        {
            throw new InvalidOperationException("The comment edit changes instruction operands.");
        }
        Instruction diagnostic = owner is GenericInstruction
            ? EditValidation.ParseInstruction(text, owner.EditingEscapeChar)
            : (Instruction)parsed;
        Instruction original = EditValidation.ParseInstruction(owner.ToString(), owner.EditingEscapeChar);
        if (!OperandRoles(original).SequenceEqual(OperandRoles(diagnostic)))
        {
            throw new InvalidOperationException("The comment edit changes instruction operand roles or values.");
        }
        EditValidation.ValidateHeredocAssociations(owner, diagnostic, plan);
        CommentToken[] actual = InstructionCollectionEditing.Descendants(parsed).OfType<CommentToken>().ToArray();
        if (expected is not null && (actual.Length != expected.Count ||
            actual.Where((comment, index) => (comment.Text ?? "") != (expected[index].Text ?? "")).Any()))
        {
            throw new InvalidOperationException("The comment edit cannot preserve comment order and values.");
        }
        plan.Commit();
    }

    /// <summary>Captures semantic boundaries that concatenated text cannot distinguish, without including comment or continuation trivia.</summary>
    /// <param name="instruction">A diagnostic parse of either the original or prospective serialization.</param>
    /// <returns>Ordered role types, semantic values, and quoting for instructions, commands, assignments, and value aggregates.</returns>
    /// <remarks>Both sides use diagnostic parsing so legacy tokenization differences do not reject otherwise safe comment edits.</remarks>
    private static IEnumerable<(Type Role, string? Value, char? Quote)> OperandRoles(Instruction instruction) =>
        InstructionCollectionEditing.Descendants(instruction).OfType<AggregateToken>()
            .Where(token => token is Instruction or Command or IKeyValuePair or IValueToken)
            .Select(token => (token.GetType(), (token as IValueToken)?.Value, (token as IQuotableToken)?.QuoteChar));

    private static string SemanticText(Token token) => token switch
    {
        CommentToken or WhitespaceToken or LineContinuationToken or NewLineToken => "",
        AggregateToken aggregate => string.Concat(aggregate.Tokens.Select(SemanticText)),
        _ => token.ToString()
    };
}
