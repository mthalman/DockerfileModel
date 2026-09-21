namespace Valleysoft.DockerfileModel.Tokens;

public class TokenList<TToken> : EditableList<TToken>
    where TToken : Token
{
    private readonly InstructionTokenListAdapter<TToken> adapter;

    internal TokenList(AggregateToken owner, Func<IEnumerable<TToken>, IEnumerable<TToken>>? filterTokens = null)
        : this(new InstructionTokenListAdapter<TToken>(owner, filterTokens))
    {
    }

    private TokenList(InstructionTokenListAdapter<TToken> adapter) : base(adapter)
    {
        this.adapter = adapter;
    }

    internal void ReplaceValue(int index, TToken replacement, TriviaDisposition trivia) =>
        adapter.ReplaceValue(index, replacement, trivia);
}
