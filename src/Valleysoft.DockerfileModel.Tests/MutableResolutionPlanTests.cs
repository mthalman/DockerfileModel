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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuppressedReferenceEditsInvalidateEarlierLiteral(bool updateInline)
    {
        VariableRefToken reference = new("UNSET", "-", "text");
        StringToken operand = reference.ModifierValueToken!.Tokens.OfType<StringToken>().Single();
        TransparentLiteral earlier = new(new Token[]
        {
            new StringToken("'"), reference, new VariableRefToken("VALUE"), new StringToken("'")
        });
        LiteralToken parent = new(new Token[] { earlier, new OperandEditingLiteral(operand) }, true, '\\');

        Assert.Equal("'${UNSET-'}expanded'custom", parent.ResolveVariables('\\', Variables,
            new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Same(reference, earlier.Tokens.ElementAt(1));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(8, false)]
    [InlineData(16, false)]
    [InlineData(4, true)]
    [InlineData(8, true)]
    [InlineData(16, true)]
    public void OrdinaryReferencesKeepBranchingPlanValidationLinear(int depth, bool updateInline)
    {
        List<CountingLiteral> literals = new();
        Token token = new StringToken("text");
        for (int i = 0; i < depth; i++)
        {
            CountingLiteral literal = new(new Token[] { token, new VariableRefToken("VALUE") });
            literals.Add(literal);
            token = literal;
        }

        Assert.Equal($"text{string.Concat(Enumerable.Repeat("expanded", depth))}",
            ((LiteralToken)token).ResolveVariables('\\', Variables,
                new ResolutionOptions { UpdateInline = updateInline }));
        Assert.InRange(literals.Sum(literal => literal.ReadCount), depth, 2 * depth);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DictionaryCallbacksStillInvalidateRawSyntax(bool customComparer, bool updateInline)
    {
        VariableRefToken reference = new("UNSET", "-", "text");
        StringToken operand = reference.ModifierValueToken!.Tokens.OfType<StringToken>().Single();
        LiteralToken earlier = new(new Token[]
        {
            new StringToken("'"), reference, new VariableRefToken("VALUE"), new StringToken("'")
        }, true, '\\');
        LiteralToken parent = new(new Token[] { earlier, new VariableRefToken("EDIT") }, true, '\\');
        EditingComparer comparer = new(operand);
        IDictionary<string, string?> variables = customComparer
            ? new Dictionary<string, string?>(comparer)
            : new EditingDictionary(operand);
        variables.Add("VALUE", "expanded");
        variables.Add("EDIT", "edit");
        comparer.Enabled = true;

        Assert.Equal("'${UNSET-'}expanded'edit", parent.ResolveVariables('\\', variables,
            new ResolutionOptions { UpdateInline = updateInline }));
    }

    [Fact]
    public void BuiltInInlineMutationStillInvalidatesSharedRawReferences()
    {
        VariableRefToken reference = new("VALUE");
        LiteralToken earlier = new(new Token[]
        {
            new StringToken("'"), reference, new VariableRefToken("OTHER"), new StringToken("'")
        }, true, '\\');
        LiteralToken parent = new(new Token[] { earlier, reference }, true, '\\');
        Dictionary<string, string?> variables = new() { ["VALUE"] = "'", ["OTHER"] = "expanded" };

        Assert.Equal("'$'expanded''", parent.ResolveVariables('\\', variables,
            new ResolutionOptions { UpdateInline = true }));
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

    private sealed class OperandEditingLiteral : LiteralToken
    {
        private readonly StringToken operand;

        public OperandEditingLiteral(StringToken operand) : base("plain")
        {
            this.operand = operand;
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            operand.Value = "'";
            return "custom";
        }
    }

    private sealed class CountingLiteral : LiteralToken
    {
        public CountingLiteral(IEnumerable<Token> tokens) : base(tokens, true, '\\')
        {
        }

        public int ReadCount { get; private set; }

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

    private sealed class EditingDictionary : Dictionary<string, string?>, IDictionary<string, string?>
    {
        private readonly StringToken operand;

        public EditingDictionary(StringToken operand)
        {
            this.operand = operand;
        }

        bool IDictionary<string, string?>.TryGetValue(string key, out string? value)
        {
            if (key == "EDIT")
            {
                operand.Value = "'";
            }
            return base.TryGetValue(key, out value);
        }
    }

    private sealed class EditingComparer : IEqualityComparer<string>
    {
        private readonly StringToken operand;

        public EditingComparer(StringToken operand)
        {
            this.operand = operand;
        }

        public bool Enabled { get; set; }

        public bool Equals(string? first, string? second) => StringComparer.Ordinal.Equals(first, second);

        public int GetHashCode(string value)
        {
            if (Enabled && value == "EDIT")
            {
                operand.Value = "'";
            }
            return StringComparer.Ordinal.GetHashCode(value);
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
        public TransparentLiteral(Token token) : this(new[] { token })
        {
        }

        public TransparentLiteral(IEnumerable<Token> tokens) : base(tokens, true, '\\')
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
