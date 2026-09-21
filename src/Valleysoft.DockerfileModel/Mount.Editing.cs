using System.Text;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public partial class Mount
{
    private EditableList<MountEntry>? entries;

    /// <summary>Gets the live, syntax-aware collection of mount fields in source order.</summary>
    /// <remarks>
    /// Duplicate keys and bare keywords remain distinct entries. Collection edits preserve
    /// unrelated tokens and repair comma separators; the final entry cannot be removed.
    /// Representations whose decoded CSV field boundaries are ambiguous are rejected without mutation.
    /// </remarks>
    public EditableList<MountEntry> Entries => entries ??= new(new EntryAdapter(this));

    /// <summary>Edits comma-delimited entry roots while keeping stable views over surviving tokens.</summary>
    private sealed class EntryAdapter(Mount owner) : IEditableListAdapter<MountEntry>
    {
        private readonly Dictionary<Token, MountEntry> views = new(ReferenceComparer<Token>.Instance);
        public IEqualityComparer<MountEntry> Comparer => ReferenceComparer<MountEntry>.Instance;

        /// <summary>Checks write eligibility without evaluating entry values or consumer serialization.</summary>
        public void ValidateWrite() => EditValidation.ValidateTree(owner);

        public IReadOnlyList<MountEntry> Snapshot()
        {
            Token[] roots = owner.Tokens.Where(MountEntry.IsEntry).ToArray();
            foreach (Token stale in views.Keys.Where(key => !ReferenceList.Contains(roots, key)).ToArray()) views.Remove(stale);
            return roots.Select(token =>
            {
                if (!views.TryGetValue(token, out MountEntry? entry)) views.Add(token, entry = new(token));
                return entry;
            }).ToArray();
        }

        public void Insert(int index, MountEntry item)
        {
            Guard.NotNull(item, nameof(item));
            var current = Snapshot();
            if (current.Any(entry => ReferenceEquals(entry.Token, item.Token)))
                throw new InvalidOperationException("An entry cannot occur twice in the same mount.");
            EditValidation.ValidateTree(item.Token);
            EditValidation.ValidateEscapeContext(item.Token, owner.escapeChar);
            TokenEditPlan plan = Prepare();
            List<Token> tokens = plan.Edit(owner);
            if (current.Count == 0) tokens.Add(item.Token);
            else if (index == current.Count)
            {
                int position = ReferenceList.IndexOf(tokens, current[current.Count - 1].Token) + 1;
                tokens.InsertRange(position, new Token[] { new SymbolToken(','), item.Token });
            }
            else
            {
                int position = ReferenceList.IndexOf(tokens, current[index].Token);
                tokens.InsertRange(position, new Token[] { item.Token, new SymbolToken(',') });
            }
            Finish(plan);
            views[item.Token] = item;
        }

        public void Replace(int index, MountEntry item, TriviaDisposition trivia)
        {
            Guard.NotNull(item, nameof(item));
            EditValidation.ValidateTrivia(trivia);
            var current = Snapshot();
            if (ReferenceEquals(current[index].Token, item.Token)) return;
            if (current.Any(entry => ReferenceEquals(entry.Token, item.Token)))
                throw new InvalidOperationException("An entry cannot occur twice in the same mount.");
            EditValidation.ValidateTree(item.Token);
            EditValidation.ValidateEscapeContext(item.Token, owner.escapeChar);
            TokenEditPlan plan = Prepare();
            List<Token> tokens = plan.Edit(owner);
            int position = ReferenceList.IndexOf(tokens, current[index].Token);
            tokens[position] = item.Token;
            if (trivia == TriviaDisposition.Preserve)
                tokens.InsertRange(position, NestedEditTrivia.Collect(current[index].Token));
            Finish(plan);
            views[item.Token] = item;
        }

        public void Remove(IReadOnlyList<int> indices, TriviaDisposition trivia)
        {
            EditValidation.ValidateTrivia(trivia);
            var current = Snapshot();
            if (indices.Count == 0) return;
            if (indices.Count == current.Count) throw new InvalidOperationException("A mount requires an entry.");
            TokenEditPlan plan = Prepare();
            List<Token> tokens = plan.Edit(owner);
            foreach (int index in indices.OrderByDescending(i => i))
            {
                Token token = current[index].Token;
                int position = ReferenceList.IndexOf(tokens, token);
                int next = tokens.FindIndex(position + 1, MountEntry.IsEntry);
                int comma = next >= 0
                    ? tokens.FindIndex(position + 1, next - position - 1, IsComma)
                    : tokens.FindLastIndex(position - 1, IsComma);
                Token? separator = comma >= 0 ? tokens[comma] : null;
                ReferenceList.Remove(tokens, token);
                if (trivia == TriviaDisposition.Preserve)
                    tokens.InsertRange(position, NestedEditTrivia.Collect(token));
                if (separator is not null) ReferenceList.Remove(tokens, separator);
            }
            Finish(plan);
        }

        public void Move(int oldIndex, int newIndex)
        {
            var current = Snapshot();
            if (oldIndex == newIndex) return;
            TokenEditPlan plan = Prepare();
            List<MountEntry> reordered = current.ToList();
            MountEntry entry = reordered[oldIndex];
            reordered.RemoveAt(oldIndex);
            reordered.Insert(newIndex, entry);
            List<Token> tokens = plan.Edit(owner);
            int i = 0;
            for (int position = 0; position < tokens.Count; position++)
                if (MountEntry.IsEntry(tokens[position])) tokens[position] = reordered[i++].Token;
            Finish(plan);
        }

        private TokenEditPlan Prepare()
        {
            EditValidation.ValidateTree(owner);
            ValidateMapping(owner.Tokens, owner.escapeChar);
            return new();
        }

        private void Finish(TokenEditPlan plan)
        {
            EditValidation.ValidateTree(new Mount(plan.Edit(owner), owner.escapeChar));
            string text = plan.Render(owner);
            Mount parsed = Parse(text, owner.escapeChar);
            var staged = plan.Edit(owner).Where(MountEntry.IsEntry).Select(token => new MountEntry(token)).ToArray();
            var actual = parsed.Tokens.Where(MountEntry.IsEntry).Select(token => new MountEntry(token)).ToArray();
            if (staged.Length != actual.Length || staged.Where((entry, i) =>
                entry.Key != actual[i].Key || entry.Value != actual[i].Value).Any())
                throw new InvalidOperationException("The edit changes mount field boundaries.");
            ValidateMapping(plan.Edit(owner), owner.escapeChar);
            plan.Commit();
        }

        private static bool IsComma(Token token) => token is SymbolToken symbol && symbol.Value == ",";
    }

    private static void ValidateMapping(IEnumerable<Token> tokens, char escapeChar)
    {
        foreach (Token token in tokens.Where(MountEntry.IsEntry))
        {
            MountEntry entry = new(token);
            string key = Decode(entry.KeyToken, escapeChar);
            string? value = entry.KeyValueToken?.ValueToken is Token valueToken ? Decode(valueToken, escapeChar) : null;
            if (key.IndexOfAny(new[] { ',', '"', '=' }) >= 0 ||
                value?.IndexOfAny(new[] { ',', '"' }) >= 0)
                throw new InvalidOperationException("Builder flag decoding and CSV fields cannot be mapped faithfully to these mount entries.");
        }
    }

    private static string Decode(Token token, char escapeChar)
    {
        string text = token.ToString(new TokenStringOptions(
            excludeLineContinuations: true, excludeQuotes: false, excludeComments: true, excludeNewLines: true));
        StringBuilder value = new();
        char? quote = null;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == escapeChar)
            {
                if (i + 1 < text.Length) value.Append(text[++i]);
            }
            else if ((ch is '\'' or '"') && (quote is null || quote == ch)) quote = quote is null ? ch : null;
            else value.Append(ch);
        }
        return value.ToString();
    }
}
