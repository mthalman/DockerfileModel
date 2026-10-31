using System.Text;

namespace Valleysoft.DockerfileModel.Tokens;

internal sealed class LiteralResolutionPlan
{
    private readonly string rawText;
    private readonly List<PendingResolution>? pending;

    public LiteralResolutionPlan(LiteralToken literal, char escapeChar, ResolutionOptions options,
        ref ShellQuoteContext context)
    {
        Literal = literal;
        EscapeChar = escapeChar;
        RemoveEscapeCharacters = options.RemoveEscapeCharacters;
        InitialContext = context;
        StringBuilder builder = new();
        char? wrapper = literal.QuoteChar;
        if (wrapper.HasValue)
        {
            context.Consume(wrapper.Value, escapeChar, out _);
            builder.Append(wrapper.Value);
        }

        // Lexical context runs forward, but resolution must retain the original reverse error/mutation order.
        for (int i = 0; i < literal.TokenList.Count; i++)
        {
            Token token = literal.TokenList[i];
            if (token is LiteralToken child)
            {
                LiteralResolutionPlan childPlan = new(child, escapeChar, options, ref context);
                (pending ??= new()).Add(new PendingResolution(i, builder.Length, child, childPlan));
            }
            else if (token is AggregateToken aggregate && !context.SuppressExpansion)
            {
                (pending ??= new()).Add(new PendingResolution(i, builder.Length, aggregate, null));
            }
            else
            {
                string raw = token.ToString();
                if (token is not (LineContinuationToken or CommentToken or NewLineToken))
                {
                    foreach (char character in raw)
                    {
                        context.Consume(character, escapeChar, out _);
                    }
                }
                builder.Append(options.FormatValue(escapeChar, raw));
            }
        }

        if (wrapper.HasValue)
        {
            context.Consume(wrapper.Value, escapeChar, out _);
            builder.Append(wrapper.Value);
        }
        rawText = builder.ToString();
    }

    public LiteralToken Literal { get; }

    public char EscapeChar { get; }

    public bool RemoveEscapeCharacters { get; }

    public ShellQuoteContext InitialContext { get; }

    public bool IsResolved { get; private set; }

    public string Resolve(char escapeChar, IDictionary<string, string?> variables, ResolutionOptions options)
    {
        IsResolved = true;
        if (pending is null)
        {
            return rawText;
        }

        StringBuilder builder = new(rawText);
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            PendingResolution operation = pending[i];
            ResolutionOptions childOptions = operation.ChildPlan is null
                ? options
                : options.WithLiteralPlan(operation.ChildPlan);
            string? value = operation.Token.ResolveVariables(escapeChar, variables, childOptions);
            if (operation.Token is VariableRefToken && options.UpdateInline)
            {
                if (value is null)
                {
                    Literal.TokenList.RemoveAt(operation.TokenIndex);
                }
                else
                {
                    Literal.TokenList[operation.TokenIndex] = new StringToken(value);
                }
            }
            builder.Insert(operation.OutputOffset, value);
        }
        return builder.ToString();
    }

    private readonly struct PendingResolution
    {
        public PendingResolution(int tokenIndex, int outputOffset, AggregateToken token, LiteralResolutionPlan? childPlan)
        {
            TokenIndex = tokenIndex;
            OutputOffset = outputOffset;
            Token = token;
            ChildPlan = childPlan;
        }

        public int TokenIndex { get; }

        public int OutputOffset { get; }

        public AggregateToken Token { get; }

        public LiteralResolutionPlan? ChildPlan { get; }
    }
}
