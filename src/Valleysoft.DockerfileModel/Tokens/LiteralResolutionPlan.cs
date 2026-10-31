using System.Text;

namespace Valleysoft.DockerfileModel.Tokens;

internal sealed class LiteralResolutionPlan
{
    private readonly LiteralToken literal;
    private readonly char? wrapper;
    private readonly List<PendingResolution>? pending;

    public LiteralResolutionPlan(LiteralToken literal, char escapeChar, ref ShellQuoteContext context)
    {
        this.literal = literal;
        wrapper = literal.QuoteChar;
        if (wrapper.HasValue)
        {
            context.Consume(wrapper.Value, escapeChar, out _);
        }

        // Lexical context runs forward, but resolution must retain the original reverse error/mutation order.
        for (int i = 0; i < literal.TokenList.Count; i++)
        {
            Token token = literal.TokenList[i];
            if (token is AggregateToken)
            {
                (pending ??= new()).Add(new PendingResolution(i, context));
            }
            ConsumeSyntax(token, escapeChar, ref context);
        }
    }

    private static void ConsumeSyntax(Token token, char escapeChar, ref ShellQuoteContext context)
    {
        // Inspect stored syntax only; consumer serialization must retain its reverse evaluation order.
        if (token is LineContinuationToken or CommentToken or NewLineToken ||
            (token is VariableRefToken && !context.SuppressExpansion))
        {
            return;
        }

        if (token is PrimitiveToken primitive)
        {
            foreach (char character in primitive.Value)
            {
                context.Consume(character, escapeChar, out _);
            }
        }
        else if (token is AggregateToken aggregate && (token is LiteralToken || context.SuppressExpansion))
        {
            char? quote = (token as IQuotableToken)?.QuoteChar;
            if (quote.HasValue)
            {
                context.Consume(quote.Value, escapeChar, out _);
            }
            if (token is VariableRefToken)
            {
                context.Consume('$', escapeChar, out _);
            }
            foreach (Token child in aggregate.Tokens)
            {
                ConsumeSyntax(child, escapeChar, ref context);
            }
            if (quote.HasValue)
            {
                context.Consume(quote.Value, escapeChar, out _);
            }
        }
    }

    public string Resolve(char escapeChar, IDictionary<string, string?> variables, ResolutionOptions options)
    {
        StringBuilder builder = new();
        int pendingIndex = (pending?.Count ?? 0) - 1;
        for (int i = literal.TokenList.Count - 1; i >= 0; i--)
        {
            Token token = literal.TokenList[i];
            ShellQuoteContext context = new();
            if (pendingIndex >= 0 && pending![pendingIndex].TokenIndex == i)
            {
                context = pending[pendingIndex--].Context;
            }

            string? value;
            if (token is LiteralToken child)
            {
                using LiteralResolutionScope scope = new(child, context);
                value = child.ResolveVariables(escapeChar, variables, options);
            }
            else if (token is AggregateToken aggregate && !context.SuppressExpansion)
            {
                value = aggregate.ResolveVariables(escapeChar, variables, options);
                if (token is VariableRefToken && options.UpdateInline)
                {
                    if (value is null)
                    {
                        literal.TokenList.RemoveAt(i);
                    }
                    else
                    {
                        literal.TokenList[i] = new StringToken(value);
                    }
                }
            }
            else
            {
                value = options.FormatValue(escapeChar, token.ToString());
            }
            builder.Insert(0, value);
        }
        if (wrapper.HasValue)
        {
            builder.Insert(0, wrapper.Value);
            builder.Append(wrapper.Value);
        }
        return builder.ToString();
    }

    private readonly struct PendingResolution
    {
        public PendingResolution(int tokenIndex, ShellQuoteContext context)
        {
            TokenIndex = tokenIndex;
            Context = context;
        }

        public int TokenIndex { get; }

        public ShellQuoteContext Context { get; }
    }
}
