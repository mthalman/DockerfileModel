using System.Runtime.ExceptionServices;

namespace Valleysoft.DockerfileModel.Tokens;

internal sealed class LiteralResolutionPlan
{
    private readonly AggregateToken token;
    private readonly char escapeChar;
    private readonly ShellQuoteContext initialContext;
    private readonly Entry[] entries;
    private readonly ExceptionDispatchInfo? quoteFailure;
    private bool consumed;

    public LiteralResolutionPlan(AggregateToken token, char escapeChar, ref ShellQuoteContext context)
    {
        this.token = token;
        this.escapeChar = escapeChar;
        initialContext = context;
        QuoteChar = ReadQuote(token, out quoteFailure);
        ConsumeQuote(QuoteChar, escapeChar, ref context);

        // Scan each subtree once, then share its plan through the original reverse virtual calls.
        entries = new Entry[token.TokenList.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            Token child = token.TokenList[i];
            ShellQuoteContext childContext = context;
            LiteralResolutionPlan? childPlan = null;
            ExceptionDispatchInfo? childFailure = null;
            if (child is not (LineContinuationToken or CommentToken or NewLineToken))
            {
                if (child is VariableRefToken)
                {
                    if (context.SuppressExpansion)
                    {
                        ConsumeRawSyntax(child, escapeChar, ref context, out childFailure);
                    }
                }
                else if (child is AggregateToken aggregate)
                {
                    childPlan = new LiteralResolutionPlan(aggregate, escapeChar, ref context);
                }
                else if (child is PrimitiveToken primitive)
                {
                    char? quote = ReadQuote(child, out childFailure);
                    ConsumeQuote(quote, escapeChar, ref context);
                    ConsumeText(primitive.Value, escapeChar, ref context);
                    ConsumeQuote(quote, escapeChar, ref context);
                }
            }
            entries[i] = new Entry(child, childContext, childPlan, childFailure);
        }
        ConsumeQuote(QuoteChar, escapeChar, ref context);
    }

    public char? QuoteChar { get; }

    public LiteralResolutionPlan ForInvocation(char escapeChar)
    {
        quoteFailure?.Throw();
        Func<char, IDictionary<string, string?>?, ResolutionOptions?, string?> resolver = token.ResolveVariables;
        bool hasOverride = resolver.Method.DeclaringType != typeof(AggregateToken);
        // Overrides can edit descendants before delegating; only the shared, non-overridden path reuses a subtree.
        if (consumed || this.escapeChar != escapeChar || hasOverride || !MatchesCurrentSyntax())
        {
            ShellQuoteContext context = initialContext;
            return new LiteralResolutionPlan(token, escapeChar, ref context);
        }
        return this;
    }

    public void BeginResolution()
    {
        quoteFailure?.Throw();
        consumed = true;
    }

    public ShellQuoteContext GetContext(int index) => entries[index].Context;

    public LiteralResolutionPlan? GetChildPlan(int index) => entries[index].Plan;

    public void ThrowDeferredFailure(int index) => entries[index].Failure?.Throw();

    private bool MatchesCurrentSyntax()
    {
        if (QuoteChar != (token as IQuotableToken)?.QuoteChar || entries.Length != token.TokenList.Count)
        {
            return false;
        }
        for (int i = 0; i < entries.Length; i++)
        {
            Token child = token.TokenList[i];
            if (!ReferenceEquals(child, entries[i].Token) ||
                (child is PrimitiveToken primitive && primitive.Value != entries[i].PrimitiveValue))
            {
                return false;
            }
        }
        return true;
    }

    private static char? ReadQuote(Token token, out ExceptionDispatchInfo? failure)
    {
        failure = null;
        try
        {
            return (token as IQuotableToken)?.QuoteChar;
        }
        catch (Exception exception)
        {
            // Metadata failures are rethrown at their reverse-resolution position, not during lexical scanning.
            failure = ExceptionDispatchInfo.Capture(exception);
            return null;
        }
    }

    private static void ConsumeRawSyntax(Token token, char escapeChar, ref ShellQuoteContext context,
        out ExceptionDispatchInfo? failure)
    {
        failure = null;
        if (token is LineContinuationToken or CommentToken or NewLineToken)
        {
            return;
        }
        char? quote = ReadQuote(token, out failure);
        ConsumeQuote(quote, escapeChar, ref context);
        if (token is VariableRefToken)
        {
            context.Consume('$', escapeChar, out _);
        }
        if (token is AggregateToken aggregate)
        {
            foreach (Token child in aggregate.Tokens)
            {
                ConsumeRawSyntax(child, escapeChar, ref context, out ExceptionDispatchInfo? childFailure);
                failure ??= childFailure;
            }
        }
        else if (token is PrimitiveToken primitive)
        {
            ConsumeText(primitive.Value, escapeChar, ref context);
        }
        ConsumeQuote(quote, escapeChar, ref context);
    }

    private static void ConsumeQuote(char? quote, char escapeChar, ref ShellQuoteContext context)
    {
        if (quote.HasValue)
        {
            context.Consume(quote.Value, escapeChar, out _);
        }
    }

    private static void ConsumeText(string text, char escapeChar, ref ShellQuoteContext context)
    {
        foreach (char character in text)
        {
            context.Consume(character, escapeChar, out _);
        }
    }

    private readonly struct Entry
    {
        public Entry(Token token, ShellQuoteContext context, LiteralResolutionPlan? plan, ExceptionDispatchInfo? failure)
        {
            Token = token;
            Context = context;
            Plan = plan;
            Failure = failure;
            PrimitiveValue = (token as PrimitiveToken)?.Value;
        }

        public Token Token { get; }
        public ShellQuoteContext Context { get; }
        public LiteralResolutionPlan? Plan { get; }
        public ExceptionDispatchInfo? Failure { get; }
        public string? PrimitiveValue { get; }
    }
}
