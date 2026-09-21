using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>Exercises structural-edit extension boundaries from a separate consumer assembly.</summary>
public class ExternalSubclassStructuralEditingTests
{
    /// <summary>Document-owner overrides cannot change the serialization that structural validation guarantees.</summary>
    /// <param name="operation">The structural document operation attempted on the consumer-defined serializer.</param>
    [Theory]
    [InlineData("insert")]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("clear")]
    [InlineData("move")]
    public void UnsupportedDocumentSerializerRejectsBeforeInvocation(string operation)
    {
        OverridingDocument document = new();
        string before = document.ToString();
        DockerfileConstruct[] items = document.Items.ToArray();
        int calls = document.SerializerCalls;

        Assert.Throws<InvalidOperationException>(() =>
        {
            switch (operation)
            {
                case "insert": document.Items.Add(new RunInstruction("echo candidate")); break;
                case "replace": document.Items[1] = new RunInstruction("echo candidate"); break;
                case "remove": document.Items.RemoveAt(1); break;
                case "clear": document.Items.Clear(); break;
                case "move": document.Items.Move(1, 0); break;
            }
        });

        Assert.Equal(calls, document.SerializerCalls);
        Assert.Equal(before, document.ToString());
        Assert.Equal(items, document.Items);
    }

    /// <summary>Document subclasses inheriting the real virtual serializer remain supported even if they hide its name.</summary>
    /// <param name="shadowMember">Whether a new nonvirtual method hides ToString only on the consumer's concrete type.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InheritedDocumentSerializationRemainsSupported(bool shadowMember)
    {
        Dockerfile document = shadowMember ? new ShadowingDocument() : new InheritedDocument();
        FromInstruction from = new("alpine");
        RunInstruction run = new("echo candidate");

        document.Items.Add(from);
        document.Items.Add(run);

        Assert.Same(from, document.Items[0]);
        Assert.Same(run, document.Items[1]);
        Assert.Equal("FROM alpine\nRUN echo candidate", document.ToString());
        Assert.Equal(document.ToString(), Dockerfile.Parse(document.ToString()).ToString());
    }

    /// <summary>Legacy constructor-seeded extensions are rejected before an edit serializes the existing document.</summary>
    /// <param name="customQuote">Whether unsupported behavior is a nested quote accessor instead of a root serializer.</param>
    /// <param name="replace">Whether to replace the existing custom item rather than append a supported item.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExistingUnsupportedDocumentTokensAreNotInvoked(bool customQuote, bool replace)
    {
        ForwardingQuoteProbe quote = new();
        OverridingInstruction serializer = new();
        Instruction existing = customQuote ? new ConsumerInstruction(quote) : serializer;
        Dockerfile document = new(new DockerfileConstruct[] { FromInstruction.Parse("FROM alpine\n"), existing });
        DockerfileConstruct[] items = document.Items.ToArray();
        string before = document.ToString();
        int getterCalls = quote.GetterCalls;
        int serializerCalls = serializer.SerializerCalls;

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (replace)
            {
                document.Items[1] = new RunInstruction("echo candidate");
            }
            else
            {
                document.Items.Add(new RunInstruction("echo candidate"));
            }
        });

        Assert.Equal(getterCalls, quote.GetterCalls);
        Assert.Equal(serializerCalls, serializer.SerializerCalls);
        Assert.Equal(items, document.Items);
        Assert.Equal(before, document.ToString());
    }

    /// <summary>Custom effective serializers cannot bypass the staged representation when adopting document items.</summary>
    /// <param name="variant">The serializer override or quote implementation hidden within the incoming tree.</param>
    /// <param name="replace">Whether adoption replaces an existing item rather than appending.</param>
    [Theory]
    [InlineData("instruction", false)]
    [InlineData("instruction", true)]
    [InlineData("inherited override", false)]
    [InlineData("inherited override", true)]
    [InlineData("aggregate", false)]
    [InlineData("aggregate", true)]
    [InlineData("literal", false)]
    [InlineData("literal", true)]
    [InlineData("variable", false)]
    [InlineData("variable", true)]
    [InlineData("primitive", false)]
    [InlineData("primitive", true)]
    [InlineData("quote aggregate", false)]
    [InlineData("quote aggregate", true)]
    public void UnsupportedSerializationRejectsDocumentAdoption(string variant, bool replace)
    {
        Dockerfile document = Dockerfile.Parse("FROM alpine\nRUN echo original\n");
        Instruction incoming = variant switch
        {
            "instruction" => new OverridingInstruction(),
            "inherited override" => new InheritedOverridingInstruction(),
            "aggregate" => new ConsumerInstruction(new OverridingAggregate()),
            "literal" => new ConsumerInstruction(new OverridingLiteral("echo candidate")),
            "variable" => new ConsumerInstruction(new OverridingVariable()),
            "primitive" => new ConsumerInstruction(new OverridingPrimitive()),
            "quote aggregate" => new ConsumerInstruction(new ConsumerQuotableAggregate()),
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        Assert.NotEqual(typeof(Token).Assembly, incoming.GetType().Assembly);
        string before = document.ToString();
        DockerfileConstruct[] items = document.Items.ToArray();
        string incomingBefore = incoming.ToString();
        Token[] incomingChildren = incoming.Tokens.ToArray();

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (replace)
            {
                document.Items[1] = incoming;
            }
            else
            {
                document.Items.Add(incoming);
            }
        });

        Assert.Equal(before, document.ToString());
        Assert.Equal(items, document.Items);
        Assert.Equal(incomingBefore, incoming.ToString());
        Assert.Equal(incomingChildren, incoming.Tokens);
        Assert.DoesNotContain(document.Items, item => ReferenceEquals(item, incoming));
    }

    /// <summary>Unsupported quote accessors are rejected without invoking consumer setters or publishing staged lists.</summary>
    /// <param name="variant">The external interface implementation, including an inherited reimplementation.</param>
    /// <param name="operation">The structural operation that would otherwise publish a staged quote.</param>
    [Theory]
    [InlineData("explicit", "insert")]
    [InlineData("explicit", "replace")]
    [InlineData("explicit", "semantic")]
    [InlineData("inherited", "insert")]
    [InlineData("inherited", "replace")]
    [InlineData("inherited", "semantic")]
    [InlineData("implicit", "insert")]
    [InlineData("implicit", "replace")]
    [InlineData("implicit", "semantic")]
    [InlineData("forwarding", "insert")]
    [InlineData("forwarding", "replace")]
    [InlineData("forwarding", "semantic")]
    public void UnsupportedQuoteImplementationsRejectAtomically(string variant, string operation)
    {
        QuoteProbe candidate = variant switch
        {
            "explicit" => new ExplicitQuoteProbe(),
            "inherited" => new InheritedQuoteProbe(),
            "implicit" => new ImplicitQuoteProbe(),
            "forwarding" => new ForwardingQuoteProbe(),
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        if (operation == "semantic")
        {
            candidate.QuoteChar = '"';
        }
        ExecFormCommand command = operation == "semantic"
            ? new ConsumerCommand(candidate)
            : ExecFormCommand.Parse("[\"original\"]");
        string before = command.ToString();
        Token[] commandChildren = command.Tokens.ToArray();
        LiteralToken[] values = command.ValueTokens.ToArray();
        string candidateBefore = candidate.ToString();
        Token[] candidateChildren = candidate.Tokens.ToArray();
        char? candidateQuote = candidate.QuoteChar;
        int getterCalls = candidate.GetterCalls;

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (operation == "insert")
            {
                command.ValueTokens.Add(candidate);
            }
            else if (operation == "replace")
            {
                command.ValueTokens[0] = candidate;
            }
            else
            {
                command.Values[0] = "replacement";
            }
        });

        Assert.Equal(0, candidate.SetterCalls);
        Assert.Equal(getterCalls, candidate.GetterCalls);
        Assert.Equal(before, command.ToString());
        Assert.Equal(commandChildren, command.Tokens);
        Assert.Equal(values, command.ValueTokens);
        Assert.Equal(candidateBefore, candidate.ToString());
        Assert.Equal(candidateChildren, candidate.Tokens);
        Assert.Equal(candidateQuote, candidate.QuoteChar);
        if (operation != "semantic")
        {
            Assert.DoesNotContain(command.ValueTokens, token => ReferenceEquals(token, candidate));
        }
    }

    /// <summary>Rejecting structural edits does not disable read-only comment inspection on an existing consumer model.</summary>
    [Fact]
    public void UnsupportedInstructionStillAllowsCommentInspection()
    {
        OverridingInstruction instruction = new();

        Assert.Empty(instruction.CommentTokens);
        Assert.Empty(instruction.Comments);
        Assert.Throws<InvalidOperationException>(() => instruction.Comments.Add("unsupported edit"));
    }

    /// <summary>Inherited serialization remains supported for external instruction roots and their subsequent comment edits.</summary>
    /// <param name="variant">The inherited aggregate, primitive, or special variable serializer nested in the instruction.</param>
    [Theory]
    [InlineData("literal")]
    [InlineData("primitive")]
    [InlineData("variable")]
    public void InheritedInstructionSerializationPreservesIdentity(string variant)
    {
        Dockerfile document = Dockerfile.Parse("FROM alpine\n");
        Token operand = variant switch
        {
            "literal" => new InheritedLiteral("echo candidate"),
            "primitive" => new InheritedPrimitive(),
            "variable" => new InheritedVariableReference(),
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        ConsumerInstruction instruction = new(operand, variant == "variable" ? "FROM" : "RUN");

        document.Items.Add(instruction);
        instruction.Comments.Add("retained");

        Assert.Same(instruction, document.Items.Last());
        Assert.Contains(instruction.Tokens, token => ReferenceEquals(token, operand));
        Assert.Equal(2, document.Items.Count);
        DockerfileParseResult parsed = Dockerfile.TryParse(document.ToString());
        Assert.True(parsed.Success);
        Assert.Equal(2, parsed.Dockerfile!.Items.Count);
        Assert.Equal(document.ToString(), parsed.Dockerfile.ToString());
    }

    /// <summary>Inherited quote implementations and unrelated hidden members do not disqualify supported consumer subclasses.</summary>
    /// <param name="shadowMembers">Whether the subtype hides members without changing effective Token or interface dispatch.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InheritedLiteralBehaviorSupportsTypedAndSemanticEdits(bool shadowMembers)
    {
        ExecFormCommand command = ExecFormCommand.Parse("[\"original\"]");
        LiteralToken candidate = shadowMembers ? new ShadowingLiteral("candidate") : new InheritedLiteral("candidate");

        command.ValueTokens.Add(candidate);
        command.Values[1] = "updated";
        command.ValueTokens.Replace(0, new InheritedLiteral("first"));

        Assert.Same(candidate, command.ValueTokens[1]);
        Assert.Equal('"', ((IQuotableToken)candidate).QuoteChar);
        Assert.Equal(new[] { "first", "updated" }, command.Values);
        Assert.Equal("[\"first\", \"updated\"]", command.ToString());
        Assert.Equal(command.Values, ExecFormCommand.Parse(command.ToString()).Values);
    }

    /// <summary>Identifier subclasses retain the built-in quote-interface implementation as well as their identity.</summary>
    [Fact]
    public void InheritedIdentifierQuoteImplementationRemainsSupported()
    {
        EnvInstruction instruction = EnvInstruction.Parse("ENV FIRST=one");
        InheritedVariable key = new("SECOND");
        InheritedLiteral value = new("two");
        KeyValueToken<Variable, LiteralToken> pair = new(key, value);

        instruction.VariableTokens.Add(pair);

        Assert.Same(pair, instruction.VariableTokens[1]);
        Assert.Same(key, pair.KeyToken);
        Assert.Same(value, pair.ValueToken);
        Assert.Equal("ENV FIRST=one SECOND=two", instruction.ToString());
    }

    /// <summary>Changes effective document serialization while leaving its stored item list unchanged.</summary>
    private sealed class OverridingDocument() : Dockerfile(new DockerfileConstruct[]
    {
        FromInstruction.Parse("FROM alpine\n"), RunInstruction.Parse("RUN echo original\n")
    })
    {
        /// <summary>Counts unsupported virtual serializer invocations during attempted edits.</summary>
        public int SerializerCalls { get; private set; }

        /// <summary>Appends an instruction absent from the model's document list.</summary>
        /// <returns>The stored constructs followed by an unmodeled RUN instruction.</returns>
        public override string ToString()
        {
            SerializerCalls++;
            return base.ToString() + "RUN echo injected\n";
        }
    }

    /// <summary>Inherits the supported document serializer unchanged.</summary>
    private class InheritedDocument : Dockerfile;

    /// <summary>Hides a name without overriding the serializer used by document operations.</summary>
    private sealed class ShadowingDocument : InheritedDocument
    {
        /// <summary>Provides an unrelated concrete-type method that structural editing does not dispatch.</summary>
        /// <returns>A shadow value not emitted by the supported document serializer.</returns>
        public new string ToString() => "shadow";
    }

    /// <summary>Provides an external instruction using only inherited concatenation semantics.</summary>
    /// <param name="operand">The instruction's operand token, retained without cloning.</param>
    /// <param name="keyword">The known instruction whose operand syntax the consumer tree represents.</param>
    private class ConsumerInstruction(Token operand, string keyword = "RUN") : Instruction(new Token[]
    {
        new KeywordToken(keyword), new WhitespaceToken(" "), operand, new NewLineToken("\n")
    });

    /// <summary>Simulates an external root serializer that emits more instructions than its child tree models.</summary>
    private class OverridingInstruction() : ConsumerInstruction(new LiteralToken("echo candidate"))
    {
        /// <summary>Counts calls through the unsupported effective serializer.</summary>
        public int SerializerCalls { get; private set; }

        /// <summary>Appends an instruction invisible to child-list-only rendering.</summary>
        /// <param name="options">The serialization options passed through the public Token entry point.</param>
        /// <returns>Two instructions despite the root's single-instruction child list.</returns>
        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            SerializerCalls++;
            return base.GetUnderlyingValue(options) + "RUN echo injected\n";
        }
    }

    /// <summary>Inherits an unsupported override from another consumer class rather than declaring one itself.</summary>
    private sealed class InheritedOverridingInstruction : OverridingInstruction;

    /// <summary>Exercises custom serialization below an otherwise supported instruction root.</summary>
    private sealed class OverridingAggregate() : AggregateToken(new Token[] { new StringToken("echo candidate") })
    {
        /// <summary>Injects additional instruction syntax from a nested aggregate.</summary>
        /// <param name="options">The requested serialization options.</param>
        /// <returns>The child text followed by an unmodeled instruction.</returns>
        protected override string GetUnderlyingValue(TokenStringOptions options) =>
            base.GetUnderlyingValue(options) + "\nRUN echo injected";
    }

    /// <summary>Exercises an override on a familiar operand type rather than an unknown aggregate type.</summary>
    /// <param name="value">The ordinary modeled operand.</param>
    private sealed class OverridingLiteral(string value) : LiteralToken(value)
    {
        /// <summary>Adds syntax not represented by the literal's children.</summary>
        /// <param name="options">The requested serialization options.</param>
        /// <returns>The modeled value followed by an unmodeled instruction.</returns>
        protected override string GetUnderlyingValue(TokenStringOptions options) =>
            base.GetUnderlyingValue(options) + "\nRUN echo injected";
    }

    /// <summary>Exercises a custom override of the renderer's special variable-prefix implementation.</summary>
    private sealed class OverridingVariable() : VariableRefToken("COMMAND")
    {
        /// <summary>Changes the special variable serialization beyond its supported prefix.</summary>
        /// <param name="options">The requested serialization options.</param>
        /// <returns>The variable reference followed by another instruction.</returns>
        protected override string GetUnderlyingValue(TokenStringOptions options) =>
            base.GetUnderlyingValue(options) + "\nRUN echo injected";
    }

    /// <summary>Demonstrates that a currently benign override still lies outside the guaranteed serializer contract.</summary>
    private sealed class OverridingPrimitive() : StringToken("echo candidate")
    {
        /// <summary>Forwards today, but is still a consumer-controlled effective serializer.</summary>
        /// <param name="options">The requested serialization options.</param>
        /// <returns>The unchanged primitive value.</returns>
        protected override string GetUnderlyingValue(TokenStringOptions options) => base.GetUnderlyingValue(options);
    }

    /// <summary>Provides consumer-defined quote behavior without a serialization override.</summary>
    private sealed class ConsumerQuotableAggregate() : AggregateToken(new Token[] { new StringToken("echo candidate") }), IQuotableToken
    {
        /// <summary>Represents quote storage that the structural publisher cannot guarantee to be side-effect-free.</summary>
        public char? QuoteChar { get; set; }
    }

    /// <summary>Shares observable consumer accessor side effects without implementing the quote interface anew.</summary>
    private abstract class QuoteProbe() : LiteralToken("candidate")
    {
        /// <summary>Counts interface getter calls during a rejected edit.</summary>
        public int GetterCalls { get; protected set; }

        /// <summary>Counts setters, including ones that mutate before throwing.</summary>
        public int SetterCalls { get; protected set; }

        /// <summary>Models an arbitrary consumer setter that cannot be rolled back safely.</summary>
        /// <param name="quote">The staged quote the publisher attempted to apply.</param>
        protected void MutateAndThrow(char? quote)
        {
            SetterCalls++;
            QuoteChar = quote;
            TokenList.Add(new StringToken("consumer mutation"));
            throw new InvalidOperationException("Consumer quote setter rejected publication.");
        }
    }

    /// <summary>Explicitly reimplements the inherited quote interface with a throwing setter.</summary>
    private class ExplicitQuoteProbe : QuoteProbe, IQuotableToken
    {
        /// <summary>Exposes consumer-controlled accessors through interface dispatch.</summary>
        char? IQuotableToken.QuoteChar
        {
            get { GetterCalls++; return QuoteChar; }
            set => MutateAndThrow(value);
        }
    }

    /// <summary>Ensures interface maps are checked even when the external implementation is inherited.</summary>
    private sealed class InheritedQuoteProbe : ExplicitQuoteProbe;

    /// <summary>Reimplements the quote interface implicitly through a newly declared property.</summary>
    private sealed class ImplicitQuoteProbe : QuoteProbe, IQuotableToken
    {
        /// <summary>Hides the built-in property and supplies the effective interface accessors.</summary>
        public new char? QuoteChar
        {
            get { GetterCalls++; return base.QuoteChar; }
            set => MutateAndThrow(value);
        }
    }

    /// <summary>Reimplements a derived interface with currently benign but consumer-controlled accessors.</summary>
    private sealed class ForwardingQuoteProbe : QuoteProbe, IQuotableValueToken
    {
        /// <summary>Forwards to built-in storage while remaining an unsupported effective interface implementation.</summary>
        char? IQuotableToken.QuoteChar
        {
            get { GetterCalls++; return QuoteChar; }
            set { SetterCalls++; QuoteChar = value; }
        }
    }

    /// <summary>Seeds a legacy low-level tree to test semantic replacement of an already-present consumer token.</summary>
    private sealed class ConsumerCommand : ExecFormCommand
    {
        /// <summary>Installs an existing consumer literal through the legacy protected token list.</summary>
        /// <param name="value">The token installed without invoking structural admission.</param>
        public ConsumerCommand(LiteralToken value) : base(new[] { "placeholder" })
        {
            TokenList[TokenList.FindIndex(token => token is LiteralToken)] = value;
        }
    }

    /// <summary>Inherits every effective serialization and quote implementation unchanged.</summary>
    /// <param name="value">The literal value.</param>
    private class InheritedLiteral(string value) : LiteralToken(value);

    /// <summary>Hides unrelated entry points without changing the Token virtual slot or inherited interface map.</summary>
    /// <param name="value">The actual literal value held by the built-in implementation.</param>
    private class ShadowingLiteral(string value) : InheritedLiteral(value)
    {
        /// <summary>Provides an unrelated overload selected only through the consumer's concrete type.</summary>
        /// <returns>A shadow value never used by the library's Token dispatch.</returns>
        public new string ToString() => "shadow";

        /// <summary>Hides the options overload without replacing the library's nonvirtual entry point.</summary>
        /// <param name="options">Unused options for the unrelated consumer method.</param>
        /// <returns>A shadow value never used by structural serialization.</returns>
        public new string ToString(TokenStringOptions options) => "shadow";

        /// <summary>Hides the protected serializer without overriding its virtual slot.</summary>
        /// <param name="options">Unused serialization options.</param>
        /// <returns>A value that must not participate in effective Token serialization.</returns>
        protected new string GetUnderlyingValue(TokenStringOptions options) => "shadow";

        /// <summary>Hides quote storage without reimplementing the inherited quote interface.</summary>
        public new char? QuoteChar { get; set; }
    }

    /// <summary>Inherits identifier serialization and built-in quote accessors.</summary>
    /// <param name="name">The environment-variable name.</param>
    private sealed class InheritedVariable(string name) : Variable(name);

    /// <summary>Inherits the supported primitive serializer from a separate assembly.</summary>
    private sealed class InheritedPrimitive() : StringToken("echo candidate");

    /// <summary>Inherits the renderer's supported dollar-prefix serializer from a separate assembly.</summary>
    private sealed class InheritedVariableReference() : VariableRefToken("COMMAND");
}
