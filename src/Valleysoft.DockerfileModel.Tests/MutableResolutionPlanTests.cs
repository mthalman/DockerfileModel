using System.Runtime.ExceptionServices;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class MutableResolutionPlanTests
{
    private static readonly Dictionary<string, string?> Variables = new() { ["VALUE"] = "expanded" };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedQuoteEditsInvalidateEarlierLiteral(bool updateInline)
    {
        QuotedPrimitive first = new("'") { QuoteChar = '"' };
        QuotedPrimitive last = new("'") { QuoteChar = '"' };
        LiteralToken earlier = new(new Token[] { first, new VariableRefToken("VALUE"), last }, true, '\\');
        LiteralToken parent = new(new Token[]
        {
            earlier, new QuoteEditingLiteral(first, last)
        }, true, '\\');

        Assert.Equal("'$VALUE'custom", parent.ResolveVariables('\\', Variables,
            new ResolutionOptions { UpdateInline = updateInline }));
    }

    [Fact]
    public void RepairedQuoteGetterDoesNotReplayOldFailure()
    {
        LiteralToken parent = new(new Token[] { new RepairingLiteral() }, true, '\\');
        Assert.Equal("\"expanded\"", parent.ResolveVariables('\\', Variables));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedDescendantEditsInvalidateEarlierLiteral(bool updateInline)
    {
        StringToken first = new("start");
        StringToken last = new("end");
        LiteralToken earlier = new(new Token[]
        {
            new LiteralToken(new Token[] { first }, false, '\\'),
            new VariableRefToken("VALUE"),
            new LiteralToken(new Token[] { last }, false, '\\')
        }, true, '\\');
        LiteralToken parent = new(new Token[] { earlier, new DescendantEditingLiteral(first, last) }, true, '\\');

        Assert.Equal("'$VALUE'custom", parent.ResolveVariables('\\', Variables,
            new ResolutionOptions { UpdateInline = updateInline }));
    }

    [Fact]
    public void SerializerCanRepairItsQuoteMetadataBeforeWrappersAreRead()
    {
        LiteralToken parent = new(new Token[] { new RepairingPrimitive() }, true, '\\');
        Assert.Equal("'plain'", parent.ResolveVariables('\\', Variables));
    }

    [Fact]
    public void ParallelDelegationSharesOperationCacheSafely()
    {
        LiteralToken parent = new(new Token[] { new ParallelLiteral() }, true, '\\') { QuoteChar = '"' };
        Assert.Equal($"\"{string.Concat(Enumerable.Repeat("'expanded'", 8))}\"",
            parent.ResolveVariables('\\', Variables));
    }

    [Fact]
    public void PrimitiveSerializationFailurePrecedesQuoteFailure()
    {
        LiteralToken parent = new(new Token[] { new DualFailurePrimitive() }, true, '\\');
        Assert.Throws<IOException>(() => parent.ResolveVariables('\\', Variables));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void TransparentOverridesReuseUnchangedSubtreePlans(int depth)
    {
        List<TransparentLiteral> tokens = new();
        Token token = new VariableRefToken("VALUE");
        for (int i = 0; i < depth; i++)
        {
            TransparentLiteral literal = new(token);
            tokens.Add(literal);
            token = literal;
        }
        Assert.Equal("expanded", ((LiteralToken)token).ResolveVariables('\\', Variables));
        for (int i = 0; i < depth - 2; i++)
        {
            Assert.NotNull(tokens[i + 1].ObservedPlan);
            Assert.Same(tokens[i + 1].ObservedPlan!.GetChildPlan(0), tokens[i].ObservedPlan);
        }
    }

    private sealed class QuotedPrimitive : PrimitiveToken, IQuotableToken
    {
        public QuotedPrimitive(string text) : base(text)
        {
        }

        public char? QuoteChar { get; set; }
    }

    private sealed class QuoteEditingLiteral : LiteralToken
    {
        private readonly QuotedPrimitive first;
        private readonly QuotedPrimitive last;

        public QuoteEditingLiteral(QuotedPrimitive first, QuotedPrimitive last) : base("plain")
        {
            this.first = first;
            this.last = last;
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            first.QuoteChar = null;
            last.QuoteChar = null;
            return "custom";
        }
    }

    private sealed class RepairingLiteral : LiteralToken
    {
        private bool repaired;

        public RepairingLiteral() : base("$VALUE", true)
        {
        }

        public override char? QuoteChar
        {
            get => repaired ? '"' : throw new InvalidOperationException("Not repaired.");
            set => base.QuoteChar = value;
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            repaired = true;
            return base.ResolveVariables(escapeChar, variables, options);
        }
    }

    private sealed class DescendantEditingLiteral : LiteralToken
    {
        private readonly StringToken first;
        private readonly StringToken last;

        public DescendantEditingLiteral(StringToken first, StringToken last) : base("plain")
        {
            this.first = first;
            this.last = last;
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            first.Value = "'";
            last.Value = "'";
            return "custom";
        }
    }

    private sealed class RepairingPrimitive : PrimitiveToken, IQuotableToken
    {
        private bool repaired;

        public RepairingPrimitive() : base("plain")
        {
        }

        public char? QuoteChar
        {
            get => repaired ? '\'' : throw new InvalidOperationException("Not repaired.");
            set => throw new NotSupportedException();
        }

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            repaired = true;
            return Value;
        }
    }

    private sealed class ParallelLiteral : LiteralToken
    {
        public ParallelLiteral() : base("'$VALUE'", true)
        {
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            string?[] results = new string?[8];
            ExceptionDispatchInfo?[] errors = new ExceptionDispatchInfo?[8];
            Thread[] workers = Enumerable.Range(0, results.Length).Select(index => new Thread(() =>
            {
                try
                {
                    results[index] = base.ResolveVariables(escapeChar, variables, options);
                }
                catch (Exception exception)
                {
                    errors[index] = ExceptionDispatchInfo.Capture(exception);
                }
            })).ToArray();
            foreach (Thread worker in workers)
            {
                worker.Start();
            }
            foreach (Thread worker in workers)
            {
                worker.Join();
            }
            foreach (ExceptionDispatchInfo? error in errors)
            {
                error?.Throw();
            }
            return string.Concat(results);
        }
    }

    private sealed class DualFailurePrimitive : PrimitiveToken, IQuotableToken
    {
        public DualFailurePrimitive() : base("plain")
        {
        }

        public char? QuoteChar
        {
            get => throw new InvalidOperationException("Quote failed.");
            set => throw new InvalidOperationException("Quote failed.");
        }

        protected override string GetUnderlyingValue(TokenStringOptions options) => throw new IOException("Serialization failed.");
    }

    private sealed class TransparentLiteral : LiteralToken
    {
        public TransparentLiteral(Token token) : base(new[] { token }, true, '\\')
        {
        }

        public LiteralResolutionPlan? ObservedPlan { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            ObservedPlan = LiteralResolutionScope.GetScope(this)?.Plan;
            return base.ResolveVariables(escapeChar, variables, options);
        }
    }
}
