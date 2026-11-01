using System.Runtime.ExceptionServices;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class ShellResolutionRegressionTests
{
    private static readonly Dictionary<string, string?> Variables = new() { ["VALUE"] = "expanded" };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AggregateBridgeRetainsEnclosingDoubleQuote(bool updateInline)
    {
        LiteralToken child = new("'$VALUE'", canContainVariables: true);
        LiteralToken parent = new(new Token[] { new BridgeToken(child) }, true, '\\') { QuoteChar = '"' };

        Assert.Equal("\"'expanded'\"", parent.ResolveVariables('\\', Variables,
            new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(updateInline ? "'expanded'" : "'$VALUE'", child.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AggregateBridgeTracksQuoteChangesInsideItsChildren(bool updateInline)
    {
        LiteralToken parent = new(new Token[]
        {
            new StringToken("'"), new BridgeToken(new LiteralToken("$VALUE'-$VALUE", true))
        }, true, '\\');

        Assert.Equal("'$VALUE'-expanded", parent.ResolveVariables('\\', Variables,
            new ResolutionOptions { UpdateInline = updateInline }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuotablePrimitiveSyntaxControlsExpansion(bool updateInline)
    {
        LiteralToken parent = new(new Token[]
        {
            new QuotedPrimitive("'") { QuoteChar = '"' }, new VariableRefToken("VALUE")
        }, true, '\\');

        Assert.Equal("\"'\"expanded", parent.ResolveVariables('\\', Variables,
            new ResolutionOptions { UpdateInline = updateInline }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RightmostRequiredFailurePrecedesLeftQuoteGetter(bool useIoException)
    {
        Exception failure = useIoException ? new IOException("Quote failed.") : new InvalidOperationException("Quote failed.");
        ThrowingQuoteLiteral child = new(failure);
        LiteralToken parent = new(new Token[]
        {
            child, new VariableRefToken("MISSING", "?", "required")
        }, true, '\\');

        Assert.Throws<VariableSubstitutionException>(() => parent.ResolveVariables('\\', Variables));
    }

    [Fact]
    public void DeferredQuoteFailureIsNotSwallowed()
    {
        InvalidOperationException failure = new("Quote failed.");
        LiteralToken parent = new(new Token[]
        {
            new ThrowingQuoteLiteral(failure), new VariableRefToken("VALUE")
        }, true, '\\');

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(
            () => parent.ResolveVariables('\\', Variables, new ResolutionOptions { UpdateInline = true })));
        Assert.Equal("expanded", Assert.IsType<StringToken>(parent.Tokens.Last()).Value);
    }

    [Fact]
    public void PrimitiveQuoteFailureRetainsReverseErrorOrder()
    {
        LiteralToken parent = new(new Token[]
        {
            new ThrowingQuotedPrimitive(), new VariableRefToken("MISSING", "?", "required")
        }, true, '\\');

        Assert.Throws<VariableSubstitutionException>(() => parent.ResolveVariables('\\', Variables));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverrideDescendantEditsRefreshCachedQuoteContexts(bool updateInline)
    {
        DescendantEditingLiteral child = new();
        LiteralToken parent = new(new Token[] { child }, true, '\\');

        Assert.Equal("'$VALUE'", parent.ResolveVariables('\\', Variables,
            new ResolutionOptions { UpdateInline = updateInline }));
        Assert.IsType<VariableRefToken>(child.Tokens.ElementAt(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedChildQuoteChangesAreSeenAtEachInvocation(bool updateInline)
    {
        QuoteSwitchingLiteral child = new();
        LiteralToken parent = new(new Token[] { child, child }, true, '\\');

        Assert.Equal(updateInline ? "'expanded'\"expanded\"" : "'$VALUE'\"expanded\"", parent.ResolveVariables('\\', Variables,
            new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(2, child.CallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkerThreadBaseDelegationRetainsContext(bool updateInline)
    {
        WorkerLiteral child = new();
        LiteralToken parent = new(new Token[] { child }, true, '\\') { QuoteChar = '"' };

        Assert.Equal("\"'expanded'\"", parent.ResolveVariables('\\', Variables,
            new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(updateInline ? "'expanded'" : "'$VALUE'", child.ToString());
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(8, false)]
    [InlineData(16, false)]
    [InlineData(4, true)]
    [InlineData(8, true)]
    [InlineData(16, true)]
    public void NestedPlansReadQuoteMetadataLinearly(int depth, bool updateInline)
    {
        List<CountingQuoteLiteral> literals = new();
        Token token = new VariableRefToken("VALUE");
        for (int i = 0; i < depth; i++)
        {
            CountingQuoteLiteral literal = new(token);
            literals.Add(literal);
            token = literal;
        }
        literals.ForEach(literal => literal.ReadCount = 0);

        Assert.Equal("expanded", ((LiteralToken)token).ResolveVariables('\\', Variables,
            new ResolutionOptions { UpdateInline = updateInline }));
        Assert.InRange(literals.Sum(literal => literal.ReadCount), depth, 2 * depth);
    }

    private sealed class BridgeToken : AggregateToken
    {
        public BridgeToken(Token child) : base(new[] { child })
        {
        }
    }

    private sealed class QuotedPrimitive : PrimitiveToken, IQuotableToken
    {
        public QuotedPrimitive(string value) : base(value)
        {
        }

        public char? QuoteChar { get; set; }
    }

    private sealed class ThrowingQuoteLiteral : LiteralToken
    {
        private readonly Exception failure;

        public ThrowingQuoteLiteral(Exception failure) : base("$VALUE", canContainVariables: true)
        {
            this.failure = failure;
        }

        public override char? QuoteChar
        {
            get => throw failure;
            set => base.QuoteChar = value;
        }
    }

    private sealed class ThrowingQuotedPrimitive : PrimitiveToken, IQuotableToken
    {
        public ThrowingQuotedPrimitive() : base("plain")
        {
        }

        public char? QuoteChar
        {
            get => throw new InvalidOperationException("Quote failed.");
            set => throw new InvalidOperationException("Quote failed.");
        }
    }

    private sealed class DescendantEditingLiteral : LiteralToken
    {
        private readonly LiteralToken prefix;

        public DescendantEditingLiteral() : this(new LiteralToken("prefix"))
        {
        }

        private DescendantEditingLiteral(LiteralToken prefix)
            : base(new Token[] { new BridgeToken(prefix), new VariableRefToken("VALUE"), new StringToken("'") }, true, '\\')
        {
            this.prefix = prefix;
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            prefix.Tokens.OfType<StringToken>().Single().Value = "'";
            return base.ResolveVariables(escapeChar, variables, options);
        }
    }

    private sealed class QuoteSwitchingLiteral : LiteralToken
    {
        public QuoteSwitchingLiteral() : base("$VALUE", canContainVariables: true)
        {
        }

        public int CallCount { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            CallCount++;
            QuoteChar = CallCount == 1 ? '"' : '\'';
            return base.ResolveVariables(escapeChar, variables, options);
        }
    }

    private sealed class WorkerLiteral : LiteralToken
    {
        public WorkerLiteral() : base("'$VALUE'", canContainVariables: true)
        {
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            string? result = null;
            ExceptionDispatchInfo? error = null;
            Thread worker = new(() =>
            {
                try
                {
                    result = base.ResolveVariables(escapeChar, variables, options);
                }
                catch (Exception exception)
                {
                    error = ExceptionDispatchInfo.Capture(exception);
                }
            });
            worker.Start();
            worker.Join();
            error?.Throw();
            return result;
        }
    }

    private sealed class CountingQuoteLiteral : LiteralToken
    {
        public CountingQuoteLiteral(Token child) : base(new[] { child }, true, '\\')
        {
        }

        public int ReadCount { get; set; }

        public override char? QuoteChar
        {
            get
            {
                ReadCount++;
                return base.QuoteChar;
            }
            set => base.QuoteChar = value;
        }
    }
}
