using System.Runtime.ExceptionServices;

namespace Valleysoft.DockerfileModel.Tokens;

internal sealed class LiteralResolutionPlan
{
    private readonly AggregateToken token;
    private readonly char escapeChar;
    private readonly ShellQuoteContext initialContext;
    private readonly Entry[] entries;
    private readonly ExceptionDispatchInfo? quoteFailure;
    private readonly Operation operation;
    private long validatedGeneration;
    private volatile bool consumed;

    public LiteralResolutionPlan(AggregateToken token, char escapeChar, ref ShellQuoteContext context)
        : this(token, escapeChar, ref context, new Operation())
    {
    }

    private LiteralResolutionPlan(AggregateToken token, char escapeChar, ref ShellQuoteContext context, Operation operation)
    {
        this.token = token;
        this.escapeChar = escapeChar;
        this.operation = operation;
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
            char? childQuote = null;
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
                    childPlan = new LiteralResolutionPlan(aggregate, escapeChar, ref context, operation);
                }
                else if (child is PrimitiveToken primitive)
                {
                    childQuote = ReadQuote(child, out childFailure);
                    ConsumeQuote(childQuote, escapeChar, ref context);
                    ConsumeText(primitive.Value, escapeChar, ref context);
                    ConsumeQuote(childQuote, escapeChar, ref context);
                }
            }
            entries[i] = new Entry(child, childContext, childPlan, childFailure, childQuote);
        }
        ConsumeQuote(QuoteChar, escapeChar, ref context);
        validatedGeneration = operation.Generation;
    }

    public char? QuoteChar { get; }

    public LiteralResolutionPlan ForInvocation(char escapeChar)
    {
        // Consumer callbacks can edit any shared subtree, even when the current token has no override.
        if (consumed || this.escapeChar != escapeChar ||
            !MatchesCurrentSyntax(recursive: Interlocked.Read(ref validatedGeneration) != operation.Generation))
        {
            ShellQuoteContext context = initialContext;
            return new LiteralResolutionPlan(token, escapeChar, ref context, operation);
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

    public void BeforeCallback(Token callbackToken) => operation.BeforeCallback(callbackToken);

    private bool MatchesCurrentSyntax(bool recursive)
    {
        char? currentQuote = ReadQuote(token, out ExceptionDispatchInfo? currentFailure);
        if (QuoteChar != currentQuote || !SameFailure(quoteFailure, currentFailure) ||
            entries.Length != token.TokenList.Count)
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
            if (child is PrimitiveToken && child is IQuotableToken)
            {
                currentQuote = ReadQuote(child, out currentFailure);
                if (entries[i].QuoteChar != currentQuote || !SameFailure(entries[i].Failure, currentFailure))
                {
                    return false;
                }
            }
            if (recursive && entries[i].Plan is LiteralResolutionPlan childPlan &&
                !childPlan.MatchesCurrentSyntax(recursive: true))
            {
                return false;
            }
        }
        Interlocked.Exchange(ref validatedGeneration, operation.Generation);
        return true;
    }

    private static bool SameFailure(ExceptionDispatchInfo? first, ExceptionDispatchInfo? second) =>
        ReferenceEquals(first?.SourceException, second?.SourceException);

    private static char? ReadQuote(Token token, out ExceptionDispatchInfo? failure)
    {
        failure = null;
        try
        {
            return (token as IQuotableToken)?.QuoteChar;
        }
        catch (Exception exception)
        {
            // Defer metadata failures until resolution, when consumer callbacks may have repaired them.
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
        public Entry(Token token, ShellQuoteContext context, LiteralResolutionPlan? plan, ExceptionDispatchInfo? failure,
            char? quoteChar)
        {
            Token = token;
            Context = context;
            Plan = plan;
            Failure = failure;
            QuoteChar = quoteChar;
            PrimitiveValue = (token as PrimitiveToken)?.Value;
        }

        public Token Token { get; }
        public ShellQuoteContext Context { get; }
        public LiteralResolutionPlan? Plan { get; }
        public ExceptionDispatchInfo? Failure { get; }
        public char? QuoteChar { get; }
        public string? PrimitiveValue { get; }
    }

    private sealed class Operation
    {
        private readonly Dictionary<Type, bool> resolverOverrides = new();
        private long generation;

        public long Generation => Interlocked.Read(ref generation);

        public void BeforeCallback(Token token)
        {
            if (token is AggregateToken aggregate)
            {
                Type type = token.GetType();
                bool hasOverride;
                lock (resolverOverrides)
                {
                    if (!resolverOverrides.TryGetValue(type, out hasOverride))
                    {
                        Func<char, IDictionary<string, string?>?, ResolutionOptions?, string?> resolver = aggregate.ResolveVariables;
                        hasOverride = resolver.Method.DeclaringType != typeof(AggregateToken);
                        resolverOverrides.Add(type, hasOverride);
                    }
                }
                if (hasOverride)
                {
                    Interlocked.Increment(ref generation);
                }
            }
            else if (token.GetType() != typeof(StringToken) && token.GetType() != typeof(SymbolToken) &&
                token.GetType() != typeof(WhitespaceToken) && token.GetType() != typeof(NewLineToken))
            {
                Interlocked.Increment(ref generation);
            }
        }
    }
}
