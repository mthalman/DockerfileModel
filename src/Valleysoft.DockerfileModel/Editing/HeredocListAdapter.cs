using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>Edits heredoc header markers and body tokens as indivisible, positionally paired definitions.</summary>
/// <param name="owner">The RUN or file-transfer instruction containing both halves of each definition.</param>
/// <param name="escapeChar">The owner's actual escape character, including a custom directive context.</param>
/// <remarks>
/// Pair views are cached by marker identity and reconciled with the live body order.
/// Writes validate complete pairing, delimiter semantics, and instruction grammar before
/// publishing changes; opaque legacy file-transfer source gaps cannot be edited speculatively.
/// </remarks>
internal sealed class HeredocListAdapter(Instruction owner, char escapeChar) : IEditableListAdapter<Heredoc>
{
    private readonly Dictionary<HeredocMarkerToken, Heredoc> views = new(ReferenceComparer<HeredocMarkerToken>.Instance);
    public IEqualityComparer<Heredoc> Comparer => ReferenceComparer<Heredoc>.Instance;

    /// <summary>Checks write eligibility without evaluating header values, pair contents, or consumer serialization.</summary>
    public void ValidateWrite() => EditValidation.ValidateTree(owner);

    /// <remarks>
    /// Reconciles cached pair views without changing their marker/body tokens.
    /// An incomplete live model exposes only its paired prefix here; write preparation rejects
    /// mismatched marker/body counts instead of treating that prefix as the complete instruction.
    /// </remarks>
    public IReadOnlyList<Heredoc> Snapshot()
    {
        var markers = owner.Tokens.OfType<HeredocMarkerToken>().ToArray();
        var bodies = owner.Tokens.OfType<HeredocBodyToken>().ToArray();
        foreach (var key in views.Keys.Where(key => !ReferenceList.Contains(markers, key)).ToArray()) views.Remove(key);
        List<Heredoc> pairs = new();
        for (int i = 0; i < Math.Min(markers.Length, bodies.Length); i++)
        {
            if (!views.TryGetValue(markers[i], out Heredoc? pair) || !ReferenceEquals(pair.Body, bodies[i]))
                views[markers[i]] = pair = new(markers[i], bodies[i]);
            pairs.Add(pair);
        }
        return pairs;
    }

    public void Insert(int index, Heredoc item)
    {
        Guard.NotNull(item, nameof(item));
        TokenEditPlan plan = Prepare();
        var current = Snapshot();
        ValidateNew(item, current);
        List<Token> tokens = plan.Edit(owner);
        if (current.Count > 0)
            ValidateMarkerBoundary(tokens, current[Math.Min(index, current.Count - 1)].Marker);
        bool terminated = owner.ToString().EndsWith("\n", StringComparison.Ordinal);
        int? firstRunInsertion = null;
        int commandStart = 0;
        if (current.Count == 0)
        {
            if (owner is RunInstruction run)
            {
                if (run.Command is not ShellFormCommand shell)
                    throw new InvalidOperationException("Heredocs require shell-form RUN.");
                int commandIndex = ReferenceList.IndexOf(tokens, shell);
                commandStart = commandIndex;
                // Textual placement does not interpret shell hashes or choose a pipeline recipient.
                firstRunInsertion = commandIndex;
                tokens.RemoveAt(commandIndex);
                tokens.InsertRange(commandIndex, shell.Tokens);
                Token[] boundaries = shell.ValueToken.Tokens.Where(token => token is NewLineToken or CommentToken).ToArray();
                if (boundaries.Length > 0)
                {
                    foreach (Token boundary in boundaries) ReferenceList.Remove(plan.Edit(shell.ValueToken), boundary);
                    tokens.InsertRange(commandIndex + shell.Tokens.Count(), boundaries);
                }
            }
            else if (tokens.OfType<SymbolToken>().Any(token => token.Value is "[" or "]"))
            {
                throw new InvalidOperationException("Heredocs cannot implicitly convert JSON file transfers.");
            }
        }

        int markerPosition;
        if (index < current.Count) markerPosition = ReferenceList.IndexOf(tokens, current[index].Marker);
        else if (current.Count > 0) markerPosition = ReferenceList.IndexOf(tokens, current[current.Count - 1].Marker) + 1;
        else if (owner is FileTransferInstruction transfer)
        {
            if (transfer.DestinationToken is not LiteralToken destination)
                throw new InvalidOperationException("A file-transfer heredoc requires a destination.");
            markerPosition = ReferenceList.IndexOf(tokens, destination);
        }
        else
        {
            markerPosition = firstRunInsertion ?? tokens.FindIndex(commandStart, token => token is NewLineToken or CommentToken);
            if (markerPosition < 0) markerPosition = tokens.Count;
        }
        if (markerPosition > 0 && !EndsWithSpace(plan.Render(tokens[markerPosition - 1])))
            tokens.Insert(markerPosition++, new WhitespaceToken(" "));
        tokens.Insert(markerPosition++, item.Marker);
        if (markerPosition < tokens.Count && tokens[markerPosition] is not NewLineToken &&
            !StartsWithSpace(tokens[markerPosition]))
            tokens.Insert(markerPosition, new WhitespaceToken(" "));

        if (current.Count == 0)
        {
            if (!tokens.Any(token => token is NewLineToken))
                tokens.Add(new NewLineToken(EditValidation.NewLine(owner.ToString())));
            tokens.Add(item.Body);
        }
        else
        {
            int bodyPosition = index < current.Count ? ReferenceList.IndexOf(tokens, current[index].Body) :
                ReferenceList.IndexOf(tokens, current[current.Count - 1].Body) + 1;
            tokens.Insert(bodyPosition, item.Body);
        }
        EnsureBodyBoundaries(plan, terminated);
        Finish(plan);
        views[item.Marker] = item;
    }

    public void Replace(int index, Heredoc item, TriviaDisposition trivia)
    {
        Guard.NotNull(item, nameof(item));
        EditValidation.ValidateTrivia(trivia);
        TokenEditPlan plan = Prepare();
        var current = Snapshot();
        Heredoc old = current[index];
        if (ReferenceEquals(old, item)) return;
        ValidateNew(item, current);
        NestedEditTrivia.RequireDiscardIfEmbedded(old.Marker, trivia);
        if (trivia == TriviaDisposition.Preserve)
            PreserveClosingTrivia(plan, old, item);
        List<Token> tokens = plan.Edit(owner);
        tokens[ReferenceList.IndexOf(tokens, old.Marker)] = item.Marker;
        tokens[ReferenceList.IndexOf(tokens, old.Body)] = item.Body;
        EnsureBodyBoundaries(plan, owner.ToString().EndsWith("\n", StringComparison.Ordinal));
        Finish(plan);
        views[item.Marker] = item;
    }

    /// <remarks>
    /// All selected marker/body pairs are removed in one plan. Removing the final RUN heredoc
    /// requires a surviving shell command; a file transfer must retain a source and destination.
    /// </remarks>
    public void Remove(IReadOnlyList<int> indices, TriviaDisposition trivia)
    {
        EditValidation.ValidateTrivia(trivia);
        TokenEditPlan plan = Prepare();
        var current = Snapshot();
        if (indices.Count == 0) return;
        List<Token> tokens = plan.Edit(owner);
        foreach (int index in indices)
        {
            Heredoc pair = current[index];
            NestedEditTrivia.RequireDiscardIfEmbedded(pair.Marker, trivia);
            ValidateMarkerBoundary(tokens, pair.Marker);
            ReferenceList.Remove(tokens, pair.Marker);
            ReferenceList.Remove(tokens, pair.Body);
        }
        if (!tokens.OfType<HeredocMarkerToken>().Any() && owner is RunInstruction)
            RestoreCommand(tokens);
        EnsureBodyBoundaries(plan, false);
        Finish(plan);
    }

    public void Move(int oldIndex, int newIndex)
    {
        TokenEditPlan plan = Prepare();
        if (oldIndex == newIndex) return;
        var current = Snapshot().ToList();
        Heredoc moved = current[oldIndex];
        current.RemoveAt(oldIndex);
        current.Insert(newIndex, moved);
        List<Token> tokens = plan.Edit(owner);
        foreach (var marker in tokens.OfType<HeredocMarkerToken>()) ValidateMarkerBoundary(tokens, marker);
        int markerIndex = 0, bodyIndex = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is HeredocMarkerToken) tokens[i] = current[markerIndex++].Marker;
            else if (tokens[i] is HeredocBodyToken) tokens[i] = current[bodyIndex++].Body;
        }
        EnsureBodyBoundaries(plan, false);
        Finish(plan);
    }

    /// <summary>Rejects malformed or unmappable live heredoc state before preparing a staged edit.</summary>
    /// <returns>An empty edit plan after the current instruction and its pair associations are validated.</returns>
    private TokenEditPlan Prepare()
    {
        EditValidation.ValidateTree(owner);
        int markers = owner.Tokens.OfType<HeredocMarkerToken>().Count();
        int bodies = owner.Tokens.OfType<HeredocBodyToken>().Count();
        if (markers != bodies) throw new InvalidOperationException("Heredoc marker/body counts do not match.");
        if (owner is FileTransferInstruction && owner.Tokens.TakeWhile(token => token is not HeredocBodyToken)
            .OfType<StringToken>().Any(token => !string.IsNullOrWhiteSpace(token.Value)))
            throw new InvalidOperationException("Opaque file-transfer header text cannot be mapped to editable source tokens; use diagnostic parsing.");
        foreach (Heredoc pair in Snapshot()) pair.ValidatePair();
        ValidateProspective(owner.ToString(), Snapshot());
        return new();
    }

    private static void ValidateNew(Heredoc item, IReadOnlyList<Heredoc> current)
    {
        item.ValidatePair();
        if (current.Any(pair => ReferenceEquals(pair.Marker, item.Marker) || ReferenceEquals(pair.Body, item.Body)))
            throw new InvalidOperationException("A heredoc pair cannot occur twice in an instruction.");
    }

    /// <summary>Retains closing-line indentation and termination independently of the selected raw body payload.</summary>
    /// <remarks>
    /// Validated pairs have at most a tab prefix and one closing newline. Existing incoming trivia
    /// must agree with preserved trivia; Discard is the explicit way to select conflicting formatting.
    /// </remarks>
    private static void PreserveClosingTrivia(TokenEditPlan plan, Heredoc old, Heredoc replacement)
    {
        List<Token> oldTokens = plan.Tokens(old.Body).ToList();
        List<Token> incoming = plan.Tokens(replacement.Body).ToList();
        int oldDelimiter = oldTokens.FindIndex(token => token is HeredocDelimiterToken);
        int newDelimiter = incoming.FindIndex(token => token is HeredocDelimiterToken);
        Token[] oldSuffix = oldTokens.Skip(oldDelimiter + 1).ToArray();
        Token[] newSuffix = incoming.Skip(newDelimiter + 1).ToArray();
        if (oldSuffix.Length > 0 && newSuffix.Length > 0 &&
            string.Concat(oldSuffix.Select(plan.Render)) != string.Concat(newSuffix.Select(plan.Render)))
            throw new InvalidOperationException("The replacement has conflicting closing-line trivia. Use Discard to select its formatting.");

        StringToken? oldPrefix = ClosingPrefix(oldTokens, oldDelimiter);
        StringToken? newPrefix = ClosingPrefix(incoming, newDelimiter);
        if (oldPrefix is not null && (!replacement.Chomp ||
            newPrefix is not null && oldPrefix.Value != newPrefix.Value))
            throw new InvalidOperationException("The replacement cannot preserve closing-delimiter indentation. Use Discard to select its formatting.");

        if (oldSuffix.Length > 0 && newSuffix.Length == 0)
            plan.Edit(replacement.Body).AddRange(oldSuffix);
        if (oldPrefix is not null && newPrefix is null)
            plan.Edit(replacement.Body).Insert(newDelimiter, oldPrefix);

        static StringToken? ClosingPrefix(List<Token> tokens, int delimiter) =>
            delimiter > 0 && tokens[delimiter - 1] is StringToken prefix &&
            prefix.Value.Length > 0 && prefix.Value.All(ch => ch == '\t') ? prefix : null;
    }

    /// <summary>Infers each missing separator from the nearest staged structural newline, excluding body payload.</summary>
    private void EnsureBodyBoundaries(TokenEditPlan plan, bool terminateLast)
    {
        var bodies = plan.Tokens(owner).OfType<HeredocBodyToken>().ToArray();
        List<(int Position, string NewLine)> boundaries = new();
        Dictionary<HeredocBodyToken, int> bodyEnds = new(ReferenceComparer<HeredocBodyToken>.Instance);
        int position = 0;
        foreach (Token token in plan.Tokens(owner))
        {
            string text = plan.Render(token);
            if (token is HeredocBodyToken body)
            {
                int childPosition = position;
                bool closingDelimiter = false;
                foreach (Token child in plan.Tokens(body))
                {
                    closingDelimiter |= child is HeredocDelimiterToken;
                    string childText = plan.Render(child);
                    if (closingDelimiter && child is NewLineToken)
                        AddBoundaries(childText, childPosition);
                    childPosition += childText.Length;
                }
                bodyEnds.Add(body, position + text.Length);
            }
            else
            {
                AddBoundaries(text, position);
            }
            position += text.Length;
        }
        for (int i = 0; i < bodies.Length; i++)
        {
            if ((i < bodies.Length - 1 || terminateLast) && !plan.Render(bodies[i]).EndsWith("\n", StringComparison.Ordinal))
            {
                string newline = boundaries.Count == 0 ? "\n" :
                    boundaries.OrderBy(boundary => Math.Abs(boundary.Position - bodyEnds[bodies[i]])).First().NewLine;
                plan.Edit(bodies[i]).Add(new NewLineToken(newline));
            }
        }

        void AddBoundaries(string text, int offset)
        {
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n')
                    boundaries.Add((offset + i, i > 0 && text[i - 1] == '\r' ? "\r\n" : "\n"));
        }
    }

    /// <summary>Validates staged ownership and paired semantics before committing the marker/body edit together.</summary>
    private void Finish(TokenEditPlan plan)
    {
        List<Token> tokens = plan.Edit(owner);
        HashSet<Token> seen = new(ReferenceComparer<Token>.Instance);
        foreach (Token token in tokens) ValidateStagedTree(token);
        var markers = tokens.OfType<HeredocMarkerToken>().ToArray();
        var bodies = tokens.OfType<HeredocBodyToken>().ToArray();
        if (markers.Length != bodies.Length) throw new InvalidOperationException("Heredoc pairs are incomplete.");
        var expected = markers.Select((marker, i) => new Heredoc(marker, bodies[i])).ToArray();
        Instruction parsed = ValidateProspective(plan.Render(owner), expected);
        if (owner is RunInstruction && expected.Length == 0)
        {
            ShellFormCommand? command = tokens.OfType<ShellFormCommand>().SingleOrDefault();
            if (command is null || parsed is not RunInstruction { Command: ShellFormCommand reparsed } ||
                command.Value != reparsed.Value)
                throw new InvalidOperationException("The restored shell command does not match the complete instruction.");
        }
        plan.Commit();

        void ValidateStagedTree(Token token)
        {
            if (!seen.Add(token))
                throw new InvalidOperationException("The edited token tree contains a cycle or a shared token.");
            if (token is AggregateToken aggregate)
                foreach (Token child in plan.Tokens(aggregate))
                    ValidateStagedTree(child);
        }
    }

    /// <summary>Uses diagnostic document parsing to verify the complete instruction and expected heredoc associations.</summary>
    /// <returns>The detached instruction used to compare restored command semantics before commit.</returns>
    /// <remarks>
    /// A temporary escape directive supplies custom context to the parser; it is not added to the owner.
    /// Raw body content and delimiter expansion/chomp semantics must survive the proposed serialization.
    /// </remarks>
    private Instruction ValidateProspective(string text, IReadOnlyList<Heredoc> expected)
    {
        string prefix = escapeChar == '`' ? "# escape=`\n" : "";
        DockerfileParseResult result = Dockerfile.TryParse(prefix + text);
        Instruction? parsed = result.Dockerfile?.Items.OfType<Instruction>().SingleOrDefault();
        bool matchingKind = (owner, parsed) is
            (RunInstruction, RunInstruction) or
            (CopyInstruction, CopyInstruction) or
            (AddInstruction, AddInstruction);
        if (!result.Success || parsed is null || !matchingKind || parsed.ToString() != text)
            throw new InvalidOperationException("The heredoc edit would not form one complete instruction.");
        IReadOnlyList<Heredoc> actual = parsed switch
        {
            RunInstruction run => run.Heredocs,
            FileTransferInstruction transfer => transfer.Heredocs,
            _ => throw new InvalidOperationException("Unsupported heredoc owner.")
        };
        if (actual.Count != expected.Count || actual.Where((pair, i) =>
            pair.Name != expected[i].Name || pair.RawContent != expected[i].RawContent ||
            pair.Chomp != expected[i].Chomp || pair.Expand != expected[i].Expand).Any())
            throw new InvalidOperationException("The edit changes heredoc associations or delimiter semantics.");
        if (parsed is FileTransferInstruction file &&
            (file.DestinationToken is null || string.IsNullOrEmpty(file.Destination) ||
                file.SourceTokens.Count + actual.Count == 0))
            throw new InvalidOperationException("A file transfer requires a source and a destination.");
        return parsed;
    }

    /// <summary>Restores the complete logical command, retaining continuation comments and surviving operand leaves.</summary>
    private void RestoreCommand(List<Token> tokens)
    {
        int start = tokens.FindIndex(token => token is KeywordToken) + 1;
        while (start < tokens.Count && (tokens[start] is WhitespaceToken && tokens[start] is not NewLineToken ||
            tokens[start] is MountFlag or NetworkFlag or SecurityFlag or LineContinuationToken or CommentToken)) start++;
        int end = tokens.FindIndex(start, token => token is NewLineToken);
        if (end < 0) end = tokens.Count;
        var commandTokens = tokens.GetRange(start, end - start);
        string text = string.Concat(commandTokens.Select(token => token.ToString()));
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Removing the final heredoc requires a surviving RUN command.");
        ShellFormCommand parsed = ShellFormCommand.Parse(text, escapeChar);
        if (parsed.ToString() != text)
            throw new InvalidOperationException("The surviving RUN command cannot be represented faithfully.");
        List<Token> children = new();
        foreach (Token token in commandTokens)
        {
            if (token is LiteralToken literal && literal.QuoteChar is null) children.AddRange(literal.Tokens);
            else children.Add(token);
        }
        ShellFormCommand command = new(new Token[] { new LiteralToken(children, false, escapeChar) }, escapeChar);
        if (command.Value != parsed.Value)
            throw new InvalidOperationException("The restored shell command changes the surviving command value.");
        tokens.RemoveRange(start, end - start);
        tokens.Insert(start, command);
    }

    private static void ValidateMarkerBoundary(List<Token> tokens, HeredocMarkerToken marker)
    {
        int index = ReferenceList.IndexOf(tokens, marker);
        if (index > 0 && !EndsWithSpace(tokens[index - 1]) ||
            index + 1 < tokens.Count && !StartsWithSpace(tokens[index + 1]))
            throw new InvalidOperationException("An inseparable shell operator or file descriptor prevents this heredoc edit.");
    }

    private static bool StartsWithSpace(Token token)
    {
        string text = token.ToString();
        return text.Length == 0 || char.IsWhiteSpace(text[0]);
    }

    private static bool EndsWithSpace(Token token)
        => EndsWithSpace(token.ToString());

    private static bool EndsWithSpace(string text)
    {
        return text.Length == 0 || char.IsWhiteSpace(text[text.Length - 1]);
    }
}
