using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class ArgResolutionContextTests
{
    [Theory]
    [InlineData(false, false, '\\', "a\\b")]
    [InlineData(false, true, '\\', "a\\b")]
    [InlineData(true, false, '\\', "a\\b")]
    [InlineData(true, true, '\\', "a\\b")]
    [InlineData(false, false, '`', "a\\\\b")]
    [InlineData(false, true, '`', "a\\\\b")]
    [InlineData(true, false, '`', "a\\\\b")]
    [InlineData(true, true, '`', "a\\\\b")]
    public void ReembeddedSubstitutionUsesCurrentEscapeCharacter(
        bool stageScope, bool updateInline, char escapeChar, string expected)
    {
        Dockerfile source = Dockerfile.Parse("ARG X=${UNSET:-\"a\\\\b\"}\nFROM alpine");
        source.ResolveVariables(options: new ResolutionOptions { UpdateInline = true });
        Token shared = Assert.Single(source.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken!.Tokens);
        Dockerfile target = Dockerfile.Parse(stageScope
            ? $"# escape={escapeChar}\nFROM alpine\nARG X=placeholder\nENV RESULT=$X"
            : $"# escape={escapeChar}\nARG X=placeholder\nFROM prefix$X");
        target.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken =
            new LiteralToken(new[] { shared }, canContainVariables: true, escapeChar);
        string original = target.ToString();

        Assert.Equal(stageScope ? $"ENV RESULT={expected}" : $"FROM prefix{expected}",
            target.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("ARG X=\"a\\\\b\"\n", source.Items.OfType<ArgInstruction>().Single().ToString());
        if (!updateInline)
        {
            Assert.Equal(original, target.ToString());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConsumerAggregateIsResolvedOnceWithOriginalOptions(bool stageScope, bool updateInline)
    {
        ResolutionOptions options = new() { UpdateInline = updateInline };
        ComputingAggregate custom = new(options);
        Dockerfile dockerfile = Document(stageScope, custom);
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=computed" : "FROM prefixcomputed",
            dockerfile.ResolveVariables(options: options));
        Assert.Equal(1, custom.ResolutionCount);
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CustomVariableCanReplaceItsEnvironment(bool stageScope, bool updateInline)
    {
        ResolutionOptions options = new() { UpdateInline = updateInline };
        Dockerfile dockerfile = Document(stageScope, new EnvironmentVariable(options));
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=custom" : "FROM prefixcustom",
            dockerfile.ResolveVariables(options: options));
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CustomVariableCanReplaceItsEscapeCharacter(bool stageScope, bool updateInline)
    {
        ResolutionOptions options = new() { UpdateInline = updateInline };
        Dockerfile dockerfile = Document(stageScope, new EscapeVariable(options));

        Assert.Equal(stageScope ? "ENV RESULT=\"a\\b\"" : "FROM prefix\"a\\b\"",
            dockerfile.ResolveVariables(options: options));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompletedReferencesFollowTheirLivePositions(bool stageScope, bool removePrefix)
    {
        VariableRefToken variable = new("UNSET", ":-", "old");
        LiteralToken right = Literal(removePrefix
            ? new Token[] { new StringToken("p"), variable } : new Token[] { variable });
        CallbackVariable left = new(() =>
        {
            if (removePrefix) right.TokenList.RemoveAt(0);
            else right.TokenList.Insert(0, new StringToken("p"));
        });
        Dockerfile dockerfile = Document(stageScope, left, right);

        Assert.Equal(stageScope ? $"ENV RESULT=left{(removePrefix ? "p" : "")}old"
            : $"FROM prefixleft{(removePrefix ? "p" : "")}old",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal(removePrefix ? "ARG X=leftold\n" : "ARG X=leftpold\n",
            dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        Assert.IsType<ResolvedStringToken>(right.TokenList[removePrefix ? 0 : 1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedReferencesFollowReorderedChildren(bool stageScope)
    {
        LiteralToken right = Literal(new VariableRefToken("A", ":-", "a"), new VariableRefToken("B", ":-", "b"));
        Dockerfile dockerfile = Document(stageScope, new CallbackVariable(() => right.TokenList.Reverse()), right);

        Assert.Equal(stageScope ? "ENV RESULT=leftab" : "FROM prefixleftab",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("ARG X=leftba\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InsertedAggregateAliasDoesNotConsumeCompletedChildCapture(bool stageScope)
    {
        LiteralToken right = Literal(new VariableRefToken("UNSET", ":-", "old"));
        LiteralToken? root = null;
        Dockerfile dockerfile = Document(stageScope, new CallbackVariable(() => root!.TokenList.Add(right)), right);
        root = dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken!;

        Assert.Equal(stageScope ? "ENV RESULT=leftold" : "FROM prefixleftold",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("ARG X=leftoldold\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        Assert.IsType<ResolvedStringToken>(Assert.Single(right.Tokens));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void PendingSuppressedReferenceUsesEditedName(bool stageScope, bool updateInline, bool singleQuoted)
    {
        VariableRefToken pending = new("OLD");
        Token[] left = singleQuoted
            ? new Token[] { new LiteralToken(new[] { pending }, true, '\\') { QuoteChar = '\'' } }
            : new Token[] { new StringToken("\\"), pending };
        Dockerfile dockerfile = Dockerfile.Parse(stageScope
            ? "FROM alpine\nARG NEW=placeholder\nARG X=placeholder\nENV RESULT=$X"
            : "ARG NEW=placeholder\nARG X=placeholder\nFROM prefix$X");
        dockerfile.Items.OfType<ArgInstruction>().Last().ArgTokens[0].ValueToken =
            Literal(left.Concat(new Token[] { new CallbackVariable(() => pending.VariableName = "NEW") }).ToArray());

        Assert.Equal(stageScope ? "ENV RESULT=$NEWleft" : "FROM prefix$NEWleft",
            dockerfile.ResolveVariables(new Dictionary<string, string?> { ["NEW"] = "$NEW" },
                new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("NEW", pending.VariableName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildExecutionContextCannotReuseCompletedModifierScope(bool updateInline)
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string?>? worker = null;
        ResolutionOptions options = new() { UpdateInline = updateInline };
        CallbackVariable callback = new(() =>
        {
            worker = Task.Run(async () =>
            {
                await gate.Task;
                return new VariableRefToken("UNSET", ":-", "$INNER").ResolveVariables('\\',
                    new Dictionary<string, string?> { ["INNER"] = "worker" }, options);
            });
        });
        Assert.Equal("FROM prefixleft", Document(false, callback).ResolveVariables(options: options));
        gate.SetResult();

        Assert.Equal("worker", await worker!.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(updateInline, options.UpdateInline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedModifierScopeIsInactiveEvenWithItsOriginalContext(bool updateInline)
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string?>? worker = null;
        ResolutionOptions options = new() { UpdateInline = updateInline };
        CallbackVariable callback = new((escapeChar, variables, receivedOptions) =>
        {
            Assert.Same(options, receivedOptions);
            IDictionary<string, string?> bindings = Assert.IsAssignableFrom<IDictionary<string, string?>>(variables);
            worker = Task.Run(async () =>
            {
                await gate.Task;
                return new VariableRefToken("UNSET", ":-", "\"$INNER\"").ResolveVariables(
                    escapeChar, bindings, options);
            });
        });
        Dockerfile dockerfile = Dockerfile.Parse("ARG INNER=worker\nARG X=placeholder\nFROM prefix$X");
        dockerfile.Items.OfType<ArgInstruction>().Last().ArgTokens[0].ValueToken = Literal(callback);
        Assert.Equal("FROM prefixleft", dockerfile.ResolveVariables(options: options));
        gate.SetResult();

        Assert.Equal("\"worker\"", await worker!.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(updateInline, options.UpdateInline);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(256)]
    public void ExplicitModifierHandlerTakesPrecedenceOverNestedScopes(int depth)
    {
        Func<LiteralToken, string?> explicitResolver = _ => "explicit";
        ResolutionOptions options = new() { ModifierValueResolver = explicitResolver };
        Dictionary<string, string?> variables = new();
        Stack<IDisposable> scopes = new();
        try
        {
            scopes.Push(options.UseModifierValueResolver('\\', variables, _ => "ambient"));
            for (int i = 1; i < depth; i++)
            {
                scopes.Push(new ResolutionOptions().UseModifierValueResolver('\\', variables, _ => "unrelated"));
            }

            Assert.Same(explicitResolver, options.GetModifierValueResolver('\\', variables));
            Assert.Equal("explicit", new VariableRefToken("UNSET", ":-", "unused")
                .ResolveVariables('\\', variables, options));
        }
        finally
        {
            while (scopes.Count > 0)
            {
                scopes.Pop().Dispose();
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConsumerOwnerQuoteGetterPreservesRequiredError(bool stageScope, bool updateInline)
    {
        bool fail = false;
        QuotedAggregate owner = new(() => fail ? throw new InvalidOperationException("Owner quote.") : null,
            new VariableRefToken("UNSET", "?", "left"), new CallbackVariable(() => fail = true));
        Dockerfile dockerfile = Document(stageScope, owner);

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal("Variable 'UNSET' is not set. Error detail: 'left'.", error.Message);
    }

    [Theory]
    [InlineData(false, false, "?")]
    [InlineData(false, true, "?")]
    [InlineData(true, false, "?")]
    [InlineData(true, true, "?")]
    [InlineData(false, false, ":?")]
    [InlineData(false, true, ":?")]
    [InlineData(true, false, ":?")]
    [InlineData(true, true, ":?")]
    public void NullCustomModifierRetainsRequiredErrorDetail(bool stageScope, bool updateInline, string modifier)
    {
        NullLiteral literal = new();
        Dockerfile dockerfile = Document(stageScope,
            new VariableRefToken("UNSET", modifier, "placeholder") { ModifierValueToken = literal });

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal("Variable 'UNSET' is not set. Error detail: '<empty>'.", error.Message);
        Assert.Equal(1, literal.ResolutionCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConsumerLeafSuppliesSerializedDefault(bool stageScope, bool updateInline)
    {
        Dockerfile dockerfile = Document(stageScope, new SerializedLeaf());
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=computed" : "FROM prefixcomputed",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PendingSuppressedSerializerFailurePreservesRequiredError(bool stageScope, bool updateInline)
    {
        bool fail = false;
        VariableRefToken suppressed = new("A", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new MutableSerializedString(() =>
                fail ? throw new InvalidOperationException("Suppressed serialization.") : "placeholder"))
        };
        Dockerfile dockerfile = Document(stageScope, new StringToken("\\"), suppressed,
            new VariableRefToken("UNSET", "?", "right"), new CallbackVariable(() => fail = true));

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal("Variable 'UNSET' is not set. Error detail: 'right'.", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingSuppressedSerializerFailureIsRethrownUnchanged(bool throwOnce)
    {
        bool fail = false;
        InvalidOperationException expected = new("Suppressed serialization.");
        VariableRefToken suppressed = new("A", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new MutableSerializedString(() =>
            {
                if (!fail) return "placeholder";
                if (throwOnce) fail = false;
                throw expected;
            }))
        };
        Dockerfile dockerfile = Document(false, new StringToken("\\"), suppressed,
            new CallbackVariable(() => fail = true));

        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => dockerfile.ResolveVariables()));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompletedReferenceCaptureFollowsMovesBetweenOwners(bool stageScope, bool moveLeft)
    {
        LiteralToken original = Literal(new VariableRefToken("UNSET", ":-", "old"));
        LiteralToken destination = Literal();
        CallbackVariable callback = new(() =>
        {
            destination.TokenList.Add(original.TokenList[0]);
            original.TokenList.RemoveAt(0);
        });
        Dockerfile dockerfile = moveLeft
            ? Document(stageScope, callback, destination, original)
            : Document(stageScope, callback, original, destination);

        Assert.Equal(stageScope ? "ENV RESULT=leftold" : "FROM prefixleftold",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("ARG X=leftold\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        Assert.IsType<ResolvedStringToken>(Assert.Single(destination.Tokens));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void PendingConsumerSerializationObservesCallbackEdits(bool stageScope, bool updateInline, bool leaf)
    {
        string text = "old";
        Token primitive = leaf ? new SerializedLeaf(() => text) : new MutableSerializedString(() => text);
        Dockerfile dockerfile = Document(stageScope, primitive, new CallbackVariable(() => text = "new"));

        Assert.Equal(stageScope ? "ENV RESULT=newleft" : "FROM prefixnewleft",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("new", text);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompletedConsumerSerializationIgnoresLaterCallbackEdits(bool stageScope, bool updateInline)
    {
        string text = "old";
        Dockerfile dockerfile = Document(stageScope, new CallbackVariable(() => text = "new"),
            new MutableSerializedString(() => text));

        Assert.Equal(stageScope ? "ENV RESULT=leftold" : "FROM prefixleftold",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("new", text);
    }

    [Fact]
    public void NonInlineStageDoesNotSerializeUnusedOriginalCaptures()
    {
        CountingContinuation continuation = new();
        VariableRefToken variable = new("UNSET", ":-", "placeholder")
        {
            ModifierValueToken = Literal(continuation, new StringToken("value"))
        };
        Dockerfile dockerfile = Document(true, variable);
        continuation.SerializationCount = 0;

        Assert.Equal("ENV RESULT=value", dockerfile.ResolveVariables());
        Assert.Equal(2, continuation.SerializationCount);
        Assert.Same(variable, Assert.Single(dockerfile.Items.OfType<ArgInstruction>()
            .Single().ArgTokens[0].ValueToken!.Tokens));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void PendingSerializerEditsRefreshEarlierSyntax(bool stageScope, bool updateInline, bool changesText)
    {
        bool active = false;
        LiteralToken pending = Literal(new VariableRefToken("UNSET", "?", "stale"));
        Dockerfile dockerfile = Document(stageScope, pending, new MutableSerializedString(() =>
        {
            if (active) pending.Value = "new";
            return active && changesText ? "q" : "p";
        }), new CallbackVariable(() => active = true));

        string value = $"new{(changesText ? "q" : "p")}left";
        Assert.Equal(stageScope ? $"ENV RESULT={value}" : $"FROM prefix{value}",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("new", pending.Value);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PendingPrimitiveQuoteFailurePreservesRequiredError(bool stageScope, bool updateInline)
    {
        bool fail = false;
        Dockerfile dockerfile = Document(stageScope, new StringToken("\\"),
            new QuotedString(() => fail ? throw new InvalidOperationException("Primitive quote.") : null),
            new VariableRefToken("UNSET", "?", "right"), new CallbackVariable(() => fail = true));

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal("Variable 'UNSET' is not set. Error detail: 'right'.", error.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PendingPrimitiveQuoteFailurePreservesSuppressedReference(bool stageScope, bool updateInline)
    {
        bool fail = false;
        InvalidOperationException expected = new("Primitive quote.");
        Dockerfile dockerfile = Document(stageScope, new StringToken("\\"),
            new QuotedString(() => fail ? throw expected : '\''),
            new VariableRefToken("UNSET", "?", "suppressed"), new StringToken("'"),
            new CallbackVariable(() => fail = true));

        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline })));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompletedCaptureLeavesDetachedOwner(bool stageScope, bool newOwner)
    {
        LiteralToken original = Literal(new VariableRefToken("UNSET", ":-", "old"));
        LiteralToken destination = Literal();
        LiteralToken? root = null;
        Dockerfile dockerfile = Document(stageScope, new CallbackVariable(() =>
        {
            Token child = Assert.Single(original.Tokens);
            root!.TokenList.Remove(original);
            if (newOwner) root.TokenList.Add(Literal(Literal(child, new VariableRefToken("UNSET", "?", "inserted"))));
            else destination.TokenList.Add(child);
        }), original, destination);
        root = dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken!;

        Assert.Equal(stageScope ? "ENV RESULT=leftold" : "FROM prefixleftold",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal(newOwner ? "ARG X=leftold${UNSET?inserted}\n" : "ARG X=leftold\n",
            dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        Assert.Equal("old", Assert.IsAssignableFrom<StringToken>(Assert.Single(original.Tokens)).Value);
    }

    private sealed class QuotedString : StringToken, IQuotableToken
    {
        private readonly Func<char?> getQuote;

        public QuotedString(Func<char?> getQuote) : base("p") => this.getQuote = getQuote;

        public char? QuoteChar { get => getQuote(); set { } }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void CallbacksObserveCompletedInlineReferences(bool stageScope, bool updateInline, bool heldReference)
    {
        VariableRefToken right = new("UNSET", ":-", "old");
        LiteralToken owner = Literal(right);
        ComputingVariable left = new(() => heldReference ? right.ToString()
            : owner.Tokens.Any(token => token is VariableRefToken) ? "pending" : "done");
        Dockerfile dockerfile = Document(stageScope, left, owner);
        string prefix = heldReference ? updateInline ? "$old" : "${UNSET:-old}"
            : updateInline ? "done" : "pending";

        Assert.Equal(stageScope ? $"ENV RESULT={prefix}old" : $"FROM prefix{prefix}old",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void InitialSerializerEditsRefreshEarlierSyntax(bool stageScope, bool updateInline)
    {
        LiteralToken pending = Literal(new VariableRefToken("UNSET", "?", "stale"));
        Dockerfile dockerfile = Document(stageScope, pending, new MutableSerializedString(() =>
        {
            pending.Value = "new";
            return "p";
        }));

        Assert.Equal(stageScope ? "ENV RESULT=newp" : "FROM prefixnewp",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
    }

    [Fact]
    public void InitialPrimitiveQuoteFailurePreservesRequiredError()
    {
        Dockerfile dockerfile = Document(true, new StringToken("\\"),
            new QuotedString(() => throw new InvalidOperationException("Initial quote.")),
            new VariableRefToken("UNSET", "?", "right"));

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));

        Assert.Equal("Variable 'UNSET' is not set. Error detail: 'right'.", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedInlineReferenceCanMoveIntoConsumerOwner(bool stageScope)
    {
        LiteralToken original = Literal(new VariableRefToken("UNSET", ":-", "old"));
        PassthroughAggregate destination = new();
        Dockerfile dockerfile = Document(stageScope, new CallbackVariable(() =>
        {
            destination.TokenList.Add(Assert.Single(original.Tokens));
            original.TokenList.Clear();
        }), original, destination);

        Assert.Equal(stageScope ? "ENV RESULT=leftold" : "FROM prefixleftold",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("ARG X=leftold\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        Assert.Equal(1, destination.ResolutionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedInlineModifierReferenceCanMoveIntoOuterDefault(bool stageScope)
    {
        LiteralToken destination = Literal();
        VariableRefToken outer = new("OUTER", ":-", "${INNER:-old}");
        LiteralToken operand = outer.ModifierValueToken!;
        Dockerfile dockerfile = Document(stageScope, new CallbackVariable(() =>
        {
            destination.TokenList.Add(Assert.Single(operand.Tokens));
            operand.TokenList.Clear();
        }), outer, destination);

        Assert.Equal(stageScope ? "ENV RESULT=leftold" : "FROM prefixleftold",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("ARG X=leftoldold\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
    }

    private sealed class ComputingVariable : VariableRefToken
    {
        private readonly Func<string> resolve;

        public ComputingVariable(Func<string> resolve) : base("LEFT") => this.resolve = resolve;

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null) => resolve();
    }

    private sealed class PassthroughAggregate : AggregateToken
    {
        public PassthroughAggregate() : base(Array.Empty<Token>()) { }

        public int ResolutionCount { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            ResolutionCount++;
            return base.ResolveVariables(escapeChar, variables, options);
        }
    }

    private sealed class CountingContinuation : LineContinuationToken
    {
        public CountingContinuation() : base("\n") { }

        public int SerializationCount { get; set; }

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            SerializationCount++;
            return base.GetUnderlyingValue(options);
        }
    }

    private sealed class SerializedLeaf : Token
    {
        private readonly Func<string> serialize;

        public SerializedLeaf(Func<string>? serialize = null) => this.serialize = serialize ?? (() => "computed");

        protected override string GetUnderlyingValue(TokenStringOptions options) => serialize();
    }

    private sealed class MutableSerializedString : StringToken
    {
        private readonly Func<string> serialize;

        public MutableSerializedString(Func<string> serialize) : base("placeholder") => this.serialize = serialize;

        protected override string GetUnderlyingValue(TokenStringOptions options) => serialize();
    }

    private static LiteralToken Literal(params Token[] tokens) =>
        new(tokens, canContainVariables: true, Dockerfile.DefaultEscapeChar);

    private static Dockerfile Document(bool stageScope, params Token[] tokens)
    {
        Dockerfile dockerfile = Dockerfile.Parse(stageScope
            ? "FROM alpine\nARG X=placeholder\nENV RESULT=$X" : "ARG X=placeholder\nFROM prefix$X");
        dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken = Literal(tokens);
        return dockerfile;
    }

    private sealed class CallbackVariable : VariableRefToken
    {
        private readonly Action<char, IDictionary<string, string?>?, ResolutionOptions?> callback;

        public CallbackVariable(Action callback) : this((_, _, _) => callback()) { }

        public CallbackVariable(Action<char, IDictionary<string, string?>?, ResolutionOptions?> callback)
            : base("LEFT") => this.callback = callback;

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            callback(escapeChar, variables, options);
            return "left";
        }
    }

    private sealed class ComputingAggregate : AggregateToken
    {
        private readonly ResolutionOptions expected;

        public ComputingAggregate(ResolutionOptions expected) : base(new[] { new StringToken("placeholder") }) =>
            this.expected = expected;

        public int ResolutionCount { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            Assert.Same(expected, options);
            ResolutionCount++;
            if (expected.UpdateInline) ReplaceWithToken(new StringToken("computed"));
            return "computed";
        }
    }

    private sealed class EnvironmentVariable : VariableRefToken
    {
        private readonly ResolutionOptions expected;

        public EnvironmentVariable(ResolutionOptions expected) : base("UNSET", ":-", "$INNER") =>
            this.expected = expected;

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            Assert.Same(expected, options);
            return base.ResolveVariables(escapeChar, new Dictionary<string, string?> { ["INNER"] = "custom" }, options);
        }
    }

    private sealed class EscapeVariable : VariableRefToken
    {
        private readonly ResolutionOptions expected;

        public EscapeVariable(ResolutionOptions expected) : base("UNSET", ":-", "\"a\\b\"") =>
            this.expected = expected;

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            Assert.Same(expected, options);
            return base.ResolveVariables('`', variables, options);
        }
    }

    private sealed class QuotedAggregate : AggregateToken, IQuotableToken
    {
        private readonly Func<char?> getQuote;

        public QuotedAggregate(Func<char?> getQuote, params Token[] tokens) : base(tokens) => this.getQuote = getQuote;

        public char? QuoteChar { get => getQuote(); set { } }
    }

    private sealed class NullLiteral : LiteralToken
    {
        public NullLiteral() : base("placeholder") { }

        public int ResolutionCount { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            ResolutionCount++;
            return null;
        }
    }
}
