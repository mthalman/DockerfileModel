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
        VariableRefToken variable = new("UNSET", ":-", "unused");
        Stack<IDisposable> scopes = new();
        try
        {
            scopes.Push(options.UseModifierValueResolver(variable, '\\', variables, _ => "ambient"));
            for (int i = 1; i < depth; i++)
            {
                scopes.Push(new ResolutionOptions().UseModifierValueResolver(new VariableRefToken("OTHER"),
                    '\\', variables, _ => "unrelated"));
            }

            Assert.Same(explicitResolver, options.GetModifierValueResolver(variable, '\\', variables));
            Assert.Equal("explicit", variable.ResolveVariables('\\', variables, options));
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
    [InlineData(false)]
    [InlineData(true)]
    public void ModifierScopesFollowLiveOwnerMembership(bool nested)
    {
        LiteralToken owner = Literal();
        LiteralToken children = nested ? Literal() : owner;
        if (nested) owner.TokenList.Add(children);
        VariableRefToken variable = new("UNSET", ":-", "\"kept\"");
        ResolutionOptions options = new();
        Dictionary<string, string?> variables = new();

        using (options.UseModifierValueResolver(owner, '\\', variables, _ => "owned"))
        {
            Assert.Equal("\"kept\"", variable.ResolveVariables('\\', variables, options));
            children.TokenList.Add(variable);
            Assert.Equal("owned", variable.ResolveVariables('\\', variables, options));
            children.TokenList.Remove(variable);
            Assert.Equal("\"kept\"", variable.ResolveVariables('\\', variables, options));
        }
    }

    [Fact]
    public void ModifierScopeOwnershipIgnoresCyclesInOpaqueOwners()
    {
        PassthroughAggregate owner = new();
        owner.TokenList.Add(owner);
        VariableRefToken variable = new("UNSET", ":-", "\"kept\"");
        ResolutionOptions options = new();
        Dictionary<string, string?> variables = new();

        using (options.UseModifierValueResolver(owner, '\\', variables, _ => "owned"))
        {
            Assert.Equal("\"kept\"", variable.ResolveVariables('\\', variables, options));
            owner.TokenList.Add(variable);
            Assert.Equal("owned", variable.ResolveVariables('\\', variables, options));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialSerializerCanReplaceEarlierSibling(bool nested)
    {
        bool edited = false;
        LiteralToken? owner = null;
        OptionSerializedString serializer = new(options =>
        {
            if (!edited && !options.ExcludeComments)
            {
                edited = true;
                owner!.TokenList[0] = new StringToken("new");
            }
            return "p";
        });
        LiteralToken value = Literal(new StringToken("old"), serializer);
        Dockerfile dockerfile = Document(true, nested ? new Token[] { value } : value.Tokens.ToArray());
        owner = nested ? value : dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken!;

        Assert.Equal("ENV RESULT=newp",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        Assert.True(edited);
        Assert.Equal("new", Assert.IsType<StringToken>(owner.TokenList[0]).Value);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PendingSerializerObservesOtherSerializerStateChanges(bool leaf, bool modifier)
    {
        bool active = false;
        Token pending = leaf ? new SerializedLeaf(() => active ? "new" : "old")
            : new MutableSerializedString(() => active ? "new" : "old");
        MutableSerializedString serializer = new(() =>
        {
            active = true;
            return "p";
        });
        Token right = modifier ? new VariableRefToken("UNSET", ":-", "placeholder")
        {
            ModifierValueToken = Literal(serializer)
        } : serializer;
        Dockerfile dockerfile = Document(true, pending, right);

        Assert.Equal("ENV RESULT=newp",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
    }

    [Fact]
    public void EarlierSerializerCannotRewriteCompletedSerializerOutput()
    {
        int serializations = 0;
        bool active = false;
        Dockerfile dockerfile = Document(true, new MutableSerializedString(() =>
        {
            active = ++serializations > 1;
            return "left";
        }), new MutableSerializedString(() => active ? "new" : "old"));

        Assert.Equal("ENV RESULT=leftold",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
    }

    [Fact]
    public void SerializerStateChangesRecheckPendingLeavesLinearly()
    {
        const int count = 512;
        int serializations = 0;
        bool active = false;
        Token[] pending = Enumerable.Range(0, count)
            .Select(_ => (Token)new MutableSerializedString(() =>
            {
                serializations++;
                return active ? "new" : "old";
            })).Append(new MutableSerializedString(() =>
            {
                serializations++;
                active = true;
                return "p";
            })).ToArray();
        Dockerfile dockerfile = Document(true, pending);

        Assert.Equal("ENV RESULT=" + String.Concat(Enumerable.Repeat("new", count)) + "p",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal(4 * count + 3, serializations);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void ModifierCallbacksCanChangeFormattingOptions(bool stageScope, bool updateInline, bool removeEscapes)
    {
        ResolutionOptions options = new()
        {
            UpdateInline = updateInline,
            RemoveEscapeCharacters = !removeEscapes
        };
        VariableRefToken reference = new("UNSET", ":-", "placeholder")
        {
            ModifierValueToken = new OptionsEditingLiteral(receivedOptions =>
            {
                Assert.Same(options, receivedOptions);
                receivedOptions.RemoveEscapeCharacters = removeEscapes;
                return "\\";
            })
        };
        Dockerfile dockerfile = Document(stageScope, reference);
        if (stageScope)
        {
            dockerfile.Items.OfType<EnvInstruction>().Single().Variables.Single().Value = "${X:+wrong}";
        }
        else
        {
            dockerfile.Items.OfType<FromInstruction>().Single().ImageName = "prefix${X:+wrong}";
        }
        string original = dockerfile.ToString();

        string value = removeEscapes ? "" : "wrong";
        Assert.Equal(stageScope ? $"ENV RESULT={value}" : $"FROM prefix{value}",
            dockerfile.ResolveVariables(options: options));
        Assert.Equal(removeEscapes, options.RemoveEscapeCharacters);
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModifierCallbacksCanDisableInlineUpdates(bool stageScope)
    {
        ResolutionOptions options = new() { UpdateInline = true };
        VariableRefToken reference = new("UNSET", ":-", "placeholder")
        {
            ModifierValueToken = new OptionsEditingLiteral(receivedOptions =>
            {
                Assert.Same(options, receivedOptions);
                receivedOptions.UpdateInline = false;
                return "new";
            })
        };
        Dockerfile dockerfile = Document(stageScope, reference);
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=new" : "FROM prefixnew",
            dockerfile.ResolveVariables(options: options));
        Assert.False(options.UpdateInline);
        Assert.Equal(original, dockerfile.ToString());
        Assert.Equal("UNSET", reference.VariableName);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SuppressedSerializersCanChangePendingDefaults(bool leaf, bool singleQuoted)
    {
        bool active = false;
        LiteralToken pending = Literal(new VariableRefToken("MISSING", "?", "stale"));
        Token first = leaf ? new SerializedLeaf(() => active ? "new" : "old") : pending;
        VariableRefToken suppressed = new("IGNORED", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new MutableSerializedString(() =>
            {
                active = true;
                if (!leaf) pending.Value = "new";
                return "p";
            }))
        };
        Dockerfile dockerfile = Document(true, first, new StringToken(singleQuoted ? "'" : "\\"),
            suppressed, new StringToken(singleQuoted ? "'" : ""));

        Assert.Equal("ENV RESULT=new${IGNORED:-p}",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
    }

    [Fact]
    public void CompletedSuppressedSerializationSurvivesPendingSyntaxEdits()
    {
        string text = "old";
        LiteralToken pending = Literal(new StringToken("old"));
        VariableRefToken suppressed = new("IGNORED", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new MutableSerializedString(() => text))
        };
        Dockerfile dockerfile = Document(true, pending, new CallbackVariable(() =>
        {
            text = "new";
            pending.Value = "new";
        }), new StringToken("\\"), suppressed);

        Assert.Equal("ENV RESULT=newleft${IGNORED:-old}",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
    }

    [Fact]
    public void PendingSuppressedReferenceCanBecomeActiveAfterCallback()
    {
        LiteralToken escape = Literal(new StringToken("\\"));
        Dockerfile dockerfile = Document(true, escape, new VariableRefToken("MISSING", "?", "active"),
            new CallbackVariable(() => escape.Value = ""));

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));

        Assert.Equal("Variable 'MISSING' is not set. Error detail: 'active'.", error.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RefreshedSerializerUsesPostSerializationQuotes(bool stageScope, bool updateInline)
    {
        bool active = false;
        Dockerfile dockerfile = Document(stageScope, new SerializationQuotedString(() => active),
            new CallbackVariable(() => active = true));

        Assert.Equal(stageScope ? "ENV RESULT=\\qleft" : "FROM prefix\\qleft",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InlineReplayPreservesEachCallbacksInlineDecision(bool stageScope, bool builtIn)
    {
        CallbackVariable left = new((_, _, options) => options!.UpdateInline = true);
        CallbackVariable right = new((_, _, options) => options!.UpdateInline = false);
        VariableRefToken middle = new("UNSET", ":-", "middle");
        Dockerfile dockerfile = Document(stageScope, builtIn
            ? new Token[] { left, middle, right } : new Token[] { left, right });

        string value = builtIn ? "leftmiddleleft" : "leftleft";
        Assert.Equal(stageScope ? $"ENV RESULT={value}" : $"FROM prefix{value}",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        LiteralToken result = dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken!;
        Assert.Same(right, result.Tokens.Last());
        if (builtIn)
        {
            Assert.Same(middle, result.Tokens.ElementAt(1));
        }
        Assert.Equal(builtIn ? "ARG X=left${UNSET:-middle}$LEFT\n" : "ARG X=left$LEFT\n",
            dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
    }

    [Fact]
    public void InlineReplayDoesNotReserializeReferencesWhoseInlineUpdatesWereDisabled()
    {
        int serializations = 0;
        CallbackVariable right = new((_, _, options) => options!.UpdateInline = false)
        {
            VariableNameToken = new OptionSerializedString(options =>
            {
                if (!options.ExcludeComments) serializations++;
                return "RIGHT";
            })
        };
        Dockerfile dockerfile = Document(true,
            new CallbackVariable((_, _, options) => options!.UpdateInline = true), right);

        Assert.Equal("ENV RESULT=leftleft",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal(1, serializations);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InitialSuppressedSerializerFailurePreservesRightmostError(bool singleQuoted, bool throwOnce)
    {
        bool failed = false;
        VariableRefToken suppressed = new("IGNORED", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new OptionSerializedString(options =>
            {
                if (options.ExcludeComments || throwOnce && failed) return "placeholder";
                failed = true;
                throw new FormatException("Suppressed serialization.");
            }))
        };
        Dockerfile dockerfile = Document(true, new StringToken(singleQuoted ? "'" : "\\"),
            suppressed, new StringToken(singleQuoted ? "'" : ""), new VariableRefToken("UNSET", "?", "right"));

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));

        Assert.Equal("Variable 'UNSET' is not set. Error detail: 'right'.", error.Message);
        Assert.True(failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialSuppressedSerializerFailureIsRethrownUnchanged(bool throwOnce)
    {
        bool failed = false;
        FormatException expected = new("Suppressed serialization.");
        VariableRefToken suppressed = new("IGNORED", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new OptionSerializedString(options =>
            {
                if (options.ExcludeComments || throwOnce && failed) return "placeholder";
                failed = true;
                throw expected;
            }))
        };
        LiteralToken prefix = Literal(new StringToken("\\"));
        Dockerfile dockerfile = Document(true, prefix, suppressed,
            new CallbackVariable(() => prefix.TokenList.Insert(0, new StringToken("new"))));

        Assert.Same(expected, Assert.Throws<FormatException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true })));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void ConsumerTriviaOverridesSupplyDefaultValues(bool stageScope, bool updateInline, bool comment)
    {
        int resolutions = 0;
        Action resolved = () => resolutions++;
        Token trivia = comment ? new ResolvingComment(resolved) : new ResolvingContinuation(resolved);
        Dockerfile dockerfile = Document(stageScope, trivia);
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=custom" : "FROM prefixcustom",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(1, resolutions);
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
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
    public void MovingPendingTokensPreservesDeferredFailures(bool betweenOwners, bool quoteGetter, bool throwOnce)
    {
        bool armed = false;
        bool failed = false;
        InvalidOperationException expected = new("Deferred token failure.");
        void Fail()
        {
            if (armed && (!throwOnce || !failed))
            {
                failed = true;
                throw expected;
            }
        }
        Token pending = quoteGetter ? new QuotedString(() =>
        {
            Fail();
            return null;
        }) : new OptionSerializedString(options =>
        {
            if (!options.ExcludeComments) Fail();
            return "p";
        });
        LiteralToken left = Literal(pending);
        LiteralToken right = Literal(new StringToken("q"));
        LiteralToken? root = null;
        CallbackVariable callback = new(() =>
        {
            if (betweenOwners)
            {
                left.TokenList.Clear();
                right.TokenList.Insert(0, pending);
            }
            else
            {
                (root!.TokenList[0], root.TokenList[1]) = (root.TokenList[1], root.TokenList[0]);
            }
        });
        Dockerfile dockerfile = Document(true, betweenOwners
            ? new Token[] { left, right, callback }
            : new Token[] { pending, new StringToken("q"), callback });
        root = dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken!;
        armed = true;

        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true })));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingPendingTokensDiscardsTheirDeferredFailures(bool quoteGetter)
    {
        bool armed = false;
        InvalidOperationException expected = new("Removed token failure.");
        Token pending = quoteGetter ? new QuotedString(() =>
            armed ? throw expected : null) : new OptionSerializedString(options =>
            armed && !options.ExcludeComments ? throw expected : "p");
        LiteralToken? root = null;
        Dockerfile dockerfile = Document(true, pending, new CallbackVariable(() =>
            root!.TokenList[0] = new StringToken("new")));
        root = dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken!;
        armed = true;

        Assert.Equal("ENV RESULT=newleft",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void CallbacksEnablingInlineUpdatesPreserveModifierQuotes(bool stageScope, bool nested, bool targetArg)
    {
        ResolutionOptions options = new();
        LiteralToken operand = Literal(new OptionsEditingLiteral(receivedOptions =>
        {
            Assert.Same(options, receivedOptions);
            receivedOptions.UpdateInline = true;
            return "a b";
        }));
        operand.QuoteChar = '"';
        VariableRefToken reference = new("UNSET", ":-", "placeholder") { ModifierValueToken = operand };
        if (nested)
        {
            reference = new VariableRefToken("OUTER", ":-", "placeholder")
            {
                ModifierValueToken = Literal(reference)
            };
        }
        Dockerfile dockerfile = Document(stageScope, reference);

        string result = targetArg
            ? dockerfile.ResolveVariables(dockerfile.Items.OfType<ArgInstruction>().Single(), options: options)
            : dockerfile.ResolveVariables(options: options);
        Assert.Equal(targetArg ? "ARG X=\"a b\"\n" : stageScope ? "ENV RESULT=a b" : "FROM prefixa b", result);
        Assert.True(options.UpdateInline);
        Assert.Equal("ARG X=\"a b\"\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModifierCallbacksLeavingInlineUpdatesDisabledPreserveSyntax(bool stageScope)
    {
        ResolutionOptions options = new();
        LiteralToken operand = Literal(new OptionsEditingLiteral(receivedOptions =>
        {
            Assert.Same(options, receivedOptions);
            return "a b";
        }));
        operand.QuoteChar = '"';
        Dockerfile dockerfile = Document(stageScope,
            new VariableRefToken("UNSET", ":-", "placeholder") { ModifierValueToken = operand });
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=a b" : "FROM prefixa b",
            dockerfile.ResolveVariables(options: options));
        Assert.False(options.UpdateInline);
        Assert.Equal(original, dockerfile.ToString());
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
    public void RenamedDeclarationsUseTheirPostResolutionName(bool stageScope, bool updateInline, bool previousBinding)
    {
        string declarations = previousBinding ? "ARG X=old\nARG X=placeholder" : "ARG X=placeholder";
        Dockerfile dockerfile = Dockerfile.Parse(stageScope
            ? $"FROM alpine\n{declarations}\nENV RESULT=${{X-fallback}}-${{Y-fallback}}"
            : $"{declarations}\nFROM prefix${{X-fallback}}-${{Y-fallback}}");
        ArgDeclaration arg = dockerfile.Items.OfType<ArgInstruction>().Last().ArgTokens[0];
        ComputingVariable variable = new(() =>
        {
            arg.Name = "Y";
            return "ok";
        });
        arg.ValueToken = Literal(variable);

        string value = previousBinding ? "old-ok" : "fallback-ok";
        Assert.Equal(stageScope ? $"ENV RESULT={value}" : $"FROM prefix{value}",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("Y", arg.Name);
        if (!updateInline)
        {
            Assert.Same(variable, Assert.Single(arg.ValueToken.Tokens));
        }
    }

    [Theory]
    [InlineData(false, '\\')]
    [InlineData(true, '\\')]
    [InlineData(false, '`')]
    [InlineData(true, '`')]
    public void MovedFailedReferencesUseTheirCurrentEscapeContext(bool initiallyQuoted, char escapeChar)
    {
        bool armed = false;
        VariableRefToken failed = new("IGNORED", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new OptionSerializedString(options =>
                armed && !options.ExcludeComments
                    ? throw new FormatException("Deferred reference serialization.") : "placeholder"))
        };
        LiteralToken left = initiallyQuoted ? Literal(failed) : Literal(new StringToken(escapeChar.ToString()), failed);
        if (initiallyQuoted) left.QuoteChar = '\'';
        LiteralToken right = Literal();
        CallbackVariable callback = new(() =>
        {
            left.TokenList.Clear();
            left.QuoteChar = null;
            right.TokenList.Add(new StringToken(escapeChar.ToString()));
            right.TokenList.Add(failed);
        });
        Dockerfile dockerfile = Dockerfile.Parse(
            $"# escape={escapeChar}\nFROM alpine\nARG X=placeholder\nENV RESULT=$X");
        dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken =
            Literal(left, right, new VariableRefToken("MISSING", "?", "right"), callback);
        armed = true;

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));

        Assert.Equal("Variable 'MISSING' is not set. Error detail: 'right'.", error.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DisabledInlineUpdatesDoNotSerializeUnusedModifierTrivia(bool stageScope, bool updateInline)
    {
        bool fail = false;
        FailingContinuation continuation = new(() => fail, new FormatException("Unused modifier syntax."));
        ResolutionOptions options = new() { UpdateInline = updateInline };
        VariableRefToken outer = new("UNSET", ":-", "placeholder")
        {
            ModifierValueToken = Literal(continuation, new CallbackVariable((_, _, receivedOptions) =>
            {
                Assert.Same(options, receivedOptions);
                receivedOptions!.UpdateInline = false;
                fail = true;
            }))
        };
        Dockerfile dockerfile = Document(stageScope, new VariableRefToken("MISSING", "?", "left"), outer);

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: options));

        Assert.Equal("Variable 'MISSING' is not set. Error detail: 'left'.", error.Message);
        Assert.False(options.UpdateInline);
        Assert.Equal(0, continuation.FailingSerializationCount);
    }

    [Fact]
    public void ReenabledInlineUpdatesDoNotSerializePreviouslyDisabledModifiers()
    {
        bool fail = false;
        FailingContinuation continuation = new(() => fail, new FormatException("Unused modifier syntax."));
        VariableRefToken outer = new("UNSET", ":-", "placeholder")
        {
            ModifierValueToken = Literal(continuation, new CallbackVariable((_, _, options) =>
            {
                options!.UpdateInline = false;
                fail = true;
            }))
        };
        Dockerfile dockerfile = Document(false,
            new CallbackVariable((_, _, options) => options!.UpdateInline = true), outer);

        Assert.Equal("FROM prefixleftleft",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        Assert.Same(outer, dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken!.Tokens.Last());
        Assert.Equal(0, continuation.FailingSerializationCount);
    }

    [Fact]
    public void DisabledInlineUpdatesStillSerializeTheReturnedInstruction()
    {
        bool fail = false;
        FormatException expected = new("Returned instruction syntax.");
        FailingContinuation continuation = new(() => fail, expected);
        VariableRefToken outer = new("UNSET", ":-", "placeholder")
        {
            ModifierValueToken = Literal(continuation, new CallbackVariable((_, _, options) =>
            {
                options!.UpdateInline = false;
                fail = true;
            }))
        };
        Dockerfile dockerfile = Document(true, outer);

        Assert.Same(expected, Assert.Throws<FormatException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true })));
        Assert.Equal(1, continuation.FailingSerializationCount);
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
    public void LaterInlineActivationRetainsEarlierModifierQuotes(bool stageScope, bool updateInline, bool nested)
    {
        LiteralToken quoted = Literal(new StringToken("a b"));
        quoted.QuoteChar = '"';
        VariableRefToken inner = new("INNER", ":-", "placeholder") { ModifierValueToken = quoted };
        VariableRefToken earlier = nested
            ? new VariableRefToken("MIDDLE", ":-", "placeholder") { ModifierValueToken = Literal(inner) }
            : inner;
        VariableRefToken outer = new("OUTER", ":-", "placeholder")
        {
            ModifierValueToken = Literal(
                new CallbackVariable((_, _, options) => options!.UpdateInline = true), earlier)
        };
        Dockerfile dockerfile = Document(stageScope, outer);

        Assert.Equal(stageScope ? "ENV RESULT=lefta b" : "FROM prefixlefta b",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("ARG X=left\"a b\"\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        if (!updateInline)
        {
            Assert.Equal(":-", inner.Modifier);
            Assert.Same(quoted, inner.ModifierValueToken);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DelayedModifierSyntaxRetainsConsumedContentsAndFormatting(bool stageScope)
    {
        LiteralToken quoted = Literal(new StringToken("a\\\\b"));
        quoted.QuoteChar = '"';
        VariableRefToken inner = new("INNER", ":-", "placeholder") { ModifierValueToken = quoted };
        VariableRefToken outer = new("OUTER", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new CallbackVariable((_, _, options) =>
            {
                options!.RemoveEscapeCharacters = true;
                options.UpdateInline = true;
                quoted.QuoteChar = '\'';
                quoted.Value = "edited";
            }), inner)
        };
        Dockerfile dockerfile = Document(stageScope, outer);

        Assert.Equal(stageScope ? "ENV RESULT=leftab" : "FROM prefixleftab",
            dockerfile.ResolveVariables(options: new ResolutionOptions()));
        Assert.Equal("ARG X=left\"a\\b\"\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        Assert.Equal(":-", inner.Modifier);
        Assert.Equal("edited", quoted.Value);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EscapedOpaqueAggregateResultsRemainSemanticData(bool stageScope, bool updateInline)
    {
        const string customValue = "\"$LITERAL\\q\"";
        Dockerfile dockerfile = Document(stageScope, new StringToken("\\"),
            new OptionsEditingLiteral(_ => customValue), new VariableRefToken("UNSET", ":-", "right"));
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? $"ENV RESULT={customValue}right" : $"FROM prefix{customValue}right",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData(false, false, '\\')]
    [InlineData(false, true, '\\')]
    [InlineData(true, false, '\\')]
    [InlineData(true, true, '\\')]
    [InlineData(false, false, '`')]
    [InlineData(false, true, '`')]
    [InlineData(true, false, '`')]
    [InlineData(true, true, '`')]
    public void OpaqueAggregatesConsumePrecedingEscapes(bool stageScope, bool updateInline, char escapeChar)
    {
        Dockerfile dockerfile = Dockerfile.Parse(stageScope
            ? $"# escape={escapeChar}\nFROM alpine\nARG X=placeholder\nENV RESULT=$X"
            : $"# escape={escapeChar}\nARG X=placeholder\nFROM prefix$X");
        dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken = Literal(
            new StringToken(escapeChar.ToString()), new OptionsEditingLiteral(_ => "computed"),
            new VariableRefToken("MISSING", "?", "right"));
        string original = dockerfile.ToString();

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal("Variable 'MISSING' is not set. Error detail: 'right'.", error.Message);
        Assert.Equal(original, dockerfile.ToString());
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
    public void DelayedModifierTriviaUsesCapturedPublicResolution(
        bool stageScope, bool updateInline, bool editNewLine)
    {
        bool fail = false;
        FailingContinuation continuation = new(() => fail, new FormatException("Deferred trivia syntax."));
        NewLineToken newLine = continuation.Tokens.OfType<NewLineToken>().Single();
        VariableRefToken inner = new("INNER", ":-", "placeholder")
        {
            ModifierValueToken = Literal(continuation, new StringToken("ok"))
        };
        VariableRefToken outer = new("OUTER", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new CallbackVariable((_, _, options) =>
            {
                options!.UpdateInline = true;
                fail = true;
                if (editNewLine) newLine.Value = "\r\n";
            }), inner)
        };
        Dockerfile dockerfile = Document(stageScope, outer);

        Assert.Equal(stageScope ? "ENV RESULT=leftok" : "FROM prefixleftok",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("ARG X=left\\\nok\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        Assert.Equal(editNewLine ? "\r\n" : "\n", newLine.Value);
        Assert.Equal(0, continuation.FailingSerializationCount);
    }

    [Theory]
    [InlineData(false, false, '\\', false)]
    [InlineData(false, false, '\\', true)]
    [InlineData(false, true, '\\', false)]
    [InlineData(false, true, '\\', true)]
    [InlineData(true, false, '\\', false)]
    [InlineData(true, false, '\\', true)]
    [InlineData(true, true, '\\', false)]
    [InlineData(true, true, '\\', true)]
    [InlineData(false, false, '`', false)]
    [InlineData(false, false, '`', true)]
    [InlineData(false, true, '`', false)]
    [InlineData(false, true, '`', true)]
    [InlineData(true, false, '`', false)]
    [InlineData(true, false, '`', true)]
    [InlineData(true, true, '`', false)]
    [InlineData(true, true, '`', true)]
    public void OpaqueAggregatesPreserveDoubleQuotedLiteralEscapes(
        bool stageScope, bool updateInline, char escapeChar, bool quoted)
    {
        Dockerfile dockerfile = Dockerfile.Parse(stageScope
            ? $"# escape={escapeChar}\nFROM alpine\nARG X=placeholder\nENV RESULT=$X"
            : $"# escape={escapeChar}\nARG X=placeholder\nFROM prefix$X");
        LiteralToken value = Literal(new StringToken(escapeChar.ToString()),
            new OptionsEditingLiteral(_ => "computed"), new StringToken("q"));
        value.QuoteChar = quoted ? '"' : null;
        dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken = value;
        string original = dockerfile.ToString();
        string expected = $"{(quoted ? escapeChar.ToString() : "")}computedq";

        Assert.Equal(stageScope ? $"ENV RESULT={expected}" : $"FROM prefix{expected}",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
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
    public void DelayedModifierSyntaxRetainsConsumedCustomNewLine(
        bool stageScope, bool updateInline, bool throwAfterConsumption)
    {
        bool consumed = false;
        int lateSerializations = 0;
        SerializedNewLine newLine = new(_ =>
        {
            if (!consumed) return "\r\n";
            lateSerializations++;
            if (throwAfterConsumption) throw new FormatException("Consumed newline syntax.");
            return "\n";
        });
        LineContinuationToken continuation = new("\n");
        continuation.TokenList[continuation.TokenList.FindIndex(token => token is NewLineToken)] = newLine;
        VariableRefToken inner = new("INNER", ":-", "placeholder")
        {
            ModifierValueToken = Literal(continuation, new StringToken("ok"))
        };
        VariableRefToken outer = new("OUTER", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new CallbackVariable((_, _, options) =>
            {
                consumed = true;
                options!.UpdateInline = true;
            }), inner)
        };
        Dockerfile dockerfile = Document(stageScope, outer);

        Assert.Equal(stageScope ? "ENV RESULT=leftok" : "FROM prefixleftok",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("ARG X=left\\\r\nok\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        Assert.Equal("\n", newLine.Value);
        Assert.Equal(0, lateSerializations);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, true, true)]
    public void IgnoredTriviaConsumesPendingConsumerSerializers(
        bool stageScope, bool updateInline, bool comment, bool consumerOwner)
    {
        string state = "old";
        MutableSerializedString text = new(() => comment ? state : state switch
        {
            "old" => " ",
            "new" => "\t",
            _ => "  "
        });
        AggregateToken trivia;
        if (comment)
        {
            CommentToken commentToken = consumerOwner ? new SerializerOnlyComment() : new CommentToken("old");
            commentToken.TextToken = text;
            trivia = commentToken;
        }
        else
        {
            LineContinuationToken continuation = consumerOwner ? new CountingContinuation() : new LineContinuationToken("\n");
            continuation.TokenList.Insert(1, text);
            trivia = continuation;
        }
        VariableRefToken inner = new("INNER", ":-", "placeholder")
        {
            ModifierValueToken = Literal(trivia, new CallbackVariable(() => state = "new"))
        };
        VariableRefToken outer = new("OUTER", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new CallbackVariable((_, _, options) =>
            {
                state = "late";
                options!.UpdateInline = true;
            }), inner)
        };
        Dockerfile dockerfile = Document(stageScope, outer);

        Assert.Equal(stageScope ? "ENV RESULT=leftleft" : "FROM prefixleftleft",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(comment ? "ARG X=left#newleft\n" : "ARG X=left\\\t\nleft\n",
            dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        Assert.Equal("late", state);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, true)]
    public void NonInlineReplayCapturesBindingsBeforeConsumerSerialization(
        bool removeBinding, bool targetArg, bool mutateBeforeConsumption, bool nestedSerializer)
    {
        IDictionary<string, string?>? captured = null;
        int serializations = 0;
        ResolutionOptions options = new() { UpdateInline = true };
        Dockerfile dockerfile = Dockerfile.Parse(
            "FROM alpine\nARG B=old\nARG SAVE=placeholder\nARG X=placeholder\nENV RESULT=$X");
        ArgInstruction[] args = dockerfile.Items.OfType<ArgInstruction>().ToArray();
        args[1].ArgTokens[0].ValueToken = Literal(new CallbackVariable((_, variables, receivedOptions) =>
        {
            captured = variables;
            Assert.Same(options, receivedOptions);
            receivedOptions!.UpdateInline = false;
        }));
        VariableRefToken right = new("UNSET", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new MutableSerializedString(() => "p"))
        };
        OptionSerializedString serializer = new(format =>
        {
            if (format.ExcludeQuotes && ++serializations == (mutateBeforeConsumption || nestedSerializer ? 1 : 2))
            {
                if (removeBinding) captured!.Remove("B");
                else captured!["B"] = "new";
            }
            return "m";
        });
        Token left = nestedSerializer ? new VariableRefToken("LEFT", ":-", "placeholder")
        {
            ModifierValueToken = Literal(serializer)
        } : serializer;
        args[2].ArgTokens[0].ValueToken = Literal(left, new VariableRefToken("B"), right);
        string original = dockerfile.ToString();

        string expected = mutateBeforeConsumption ? removeBinding ? "mp" : "mnewp" : "moldp";
        Assert.Equal(targetArg ? $"ARG X={expected}\n" : $"ENV RESULT={expected}",
            targetArg ? dockerfile.ResolveVariables(args[2], options: options)
                : dockerfile.ResolveVariables(options: options));
        Assert.Equal(nestedSerializer ? 1 : 2, serializations);
        Assert.False(options.UpdateInline);
        Assert.Equal(!removeBinding, captured!.ContainsKey("B"));
        if (!removeBinding) Assert.Equal("new", captured["B"]);
        Assert.Equal(original, dockerfile.ToString());
    }

    [Theory]
    [InlineData(false, false, false, 0)]
    [InlineData(false, false, false, 1)]
    [InlineData(false, false, false, 2)]
    [InlineData(false, false, true, 0)]
    [InlineData(false, false, true, 1)]
    [InlineData(false, false, true, 2)]
    [InlineData(false, true, false, 0)]
    [InlineData(false, true, false, 1)]
    [InlineData(false, true, false, 2)]
    [InlineData(false, true, true, 0)]
    [InlineData(false, true, true, 1)]
    [InlineData(false, true, true, 2)]
    [InlineData(true, false, false, 0)]
    [InlineData(true, false, false, 1)]
    [InlineData(true, false, false, 2)]
    [InlineData(true, false, true, 0)]
    [InlineData(true, false, true, 1)]
    [InlineData(true, false, true, 2)]
    [InlineData(true, true, false, 0)]
    [InlineData(true, true, false, 1)]
    [InlineData(true, true, false, 2)]
    [InlineData(true, true, true, 0)]
    [InlineData(true, true, true, 1)]
    [InlineData(true, true, true, 2)]
    public void IgnoredTriviaConsumesCustomAggregateCallbacks(
        bool stageScope, bool updateInline, bool comment, int callbackKind)
    {
        LiteralToken pending = Literal(new VariableRefToken("MISSING", "?", "pending"));
        int calls = 0;
        ResolutionOptions options = new() { UpdateInline = updateInline };
        void Consume(ResolutionOptions? receivedOptions)
        {
            Assert.Same(options, receivedOptions);
            calls++;
            pending.Value = "new";
        }
        AggregateToken callback;
        if (callbackKind == 0)
        {
            callback = new OptionsEditingLiteral(receivedOptions =>
            {
                Consume(receivedOptions);
                return "computed";
            });
        }
        else
        {
            CallbackVariable variable = new((_, _, receivedOptions) => Consume(receivedOptions));
            if (callbackKind == 1) callback = variable;
            else
            {
                PassthroughAggregate aggregate = new();
                aggregate.TokenList.Add(variable);
                callback = aggregate;
            }
        }
        AggregateToken trivia;
        if (comment)
        {
            SerializerOnlyComment owner = new();
            owner.TokenList.Add(callback);
            trivia = owner;
        }
        else
        {
            CountingContinuation owner = new();
            owner.TokenList.Insert(1, callback);
            trivia = owner;
        }
        Dockerfile dockerfile = Document(stageScope, pending, trivia);

        if (stageScope && !updateInline)
        {
            VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
                dockerfile.ResolveVariables(options: options));
            Assert.Equal("Variable 'MISSING' is not set. Error detail: 'pending'.", error.Message);
        }
        else
        {
            Assert.Equal(stageScope ? "ENV RESULT=new" : "FROM prefixnew",
                dockerfile.ResolveVariables(options: options));
        }
        Assert.Equal(1, calls);
        Assert.Equal("new", pending.Value);
        if (callback is PassthroughAggregate passthrough) Assert.Equal(1, passthrough.ResolutionCount);
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
    public void IgnoredTriviaCustomCallbacksRetainConsumedModifierSyntax(
        bool stageScope, bool updateInline, bool comment)
    {
        string state = "consumed";
        int calls = 0;
        ResolutionOptions options = new() { UpdateInline = updateInline };
        OptionsEditingLiteral callback = new(receivedOptions =>
        {
            Assert.Same(options, receivedOptions);
            calls++;
            return state;
        }) { Value = String.Empty };
        AggregateToken trivia;
        if (comment)
        {
            CommentToken owner = new(String.Empty);
            owner.TokenList.Add(callback);
            trivia = owner;
        }
        else
        {
            CountingContinuation owner = new();
            owner.TokenList.Insert(1, callback);
            trivia = owner;
        }
        VariableRefToken inner = new("INNER", ":-", "placeholder")
        {
            ModifierValueToken = Literal(trivia, new StringToken("ok"))
        };
        VariableRefToken outer = new("OUTER", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new CallbackVariable((_, _, receivedOptions) =>
            {
                state = "late";
                receivedOptions!.UpdateInline = true;
            }), inner)
        };
        Dockerfile dockerfile = Document(stageScope, outer);

        Assert.Equal(stageScope ? "ENV RESULT=leftok" : "FROM prefixleftok",
            dockerfile.ResolveVariables(options: options));
        Assert.Equal(comment ? "ARG X=left#consumedok\n" : "ARG X=left\\consumed\nok\n",
            dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        Assert.Equal(1, calls);
        Assert.Equal("late", state);
        Assert.Equal(String.Empty, callback.Value);
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
    public void IgnoredTriviaCallbacksCanDisablePendingInlineUpdates(
        bool stageScope, bool updateInline, bool comment)
    {
        int calls = 0;
        ResolutionOptions options = new() { UpdateInline = updateInline };
        CallbackVariable callback = new((_, _, receivedOptions) =>
        {
            Assert.Same(options, receivedOptions);
            calls++;
            receivedOptions!.UpdateInline = false;
        });
        AggregateToken trivia;
        if (comment)
        {
            CommentToken owner = new(String.Empty);
            owner.TokenList.Add(callback);
            trivia = owner;
        }
        else
        {
            LineContinuationToken owner = new("\n");
            owner.TokenList.Insert(1, callback);
            trivia = owner;
        }
        VariableRefToken pending = new("MISSING", ":-", "ok");
        Dockerfile dockerfile = Document(stageScope, pending, trivia);
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=ok" : "FROM prefixok",
            dockerfile.ResolveVariables(options: options));
        Assert.Equal(1, calls);
        Assert.False(options.UpdateInline);
        Assert.Equal(original, dockerfile.ToString());
        Assert.Equal("ok", pending.ModifierValue);
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
    public void InactiveModifiersDoNotConsumeIgnoredTriviaCallbacks(
        bool stageScope, bool updateInline, bool comment)
    {
        int calls = 0;
        OptionsEditingLiteral callback = new(_ =>
        {
            calls++;
            throw new FormatException("Inactive trivia callback.");
        }) { Value = String.Empty };
        AggregateToken trivia;
        if (comment)
        {
            CommentToken owner = new(String.Empty);
            owner.TokenList.Add(callback);
            trivia = owner;
        }
        else
        {
            LineContinuationToken owner = new("\n");
            owner.TokenList.Insert(1, callback);
            trivia = owner;
        }
        VariableRefToken variable = new("UNSET", ":+", "placeholder")
        {
            ModifierValueToken = Literal(trivia)
        };
        Dockerfile dockerfile = Document(stageScope, variable);
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=" : "FROM prefix",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(0, calls);
        if (!updateInline) Assert.Equal(original, dockerfile.ToString());
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, true, true)]
    public void IgnoredTriviaCallbacksPreserveRightmostErrorPrecedence(
        bool stageScope, bool updateInline, bool comment, bool callbackOnRight)
    {
        int calls = 0;
        FormatException expected = new("Trivia callback.");
        OptionsEditingLiteral callback = new(_ =>
        {
            calls++;
            throw expected;
        }) { Value = String.Empty };
        AggregateToken trivia;
        if (comment)
        {
            CommentToken owner = new(String.Empty);
            owner.TokenList.Add(callback);
            trivia = owner;
        }
        else
        {
            LineContinuationToken owner = new("\n");
            owner.TokenList.Insert(1, callback);
            trivia = owner;
        }
        VariableRefToken required = new("MISSING", "?", "required");
        Dockerfile dockerfile = callbackOnRight ? Document(stageScope, required, trivia)
            : Document(stageScope, trivia, required);
        string original = dockerfile.ToString();
        ResolutionOptions options = new() { UpdateInline = updateInline };

        if (callbackOnRight)
        {
            Assert.Same(expected, Assert.Throws<FormatException>(() =>
                dockerfile.ResolveVariables(options: options)));
            Assert.Equal(1, calls);
        }
        else
        {
            VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
                dockerfile.ResolveVariables(options: options));
            Assert.Equal("Variable 'MISSING' is not set. Error detail: 'required'.", error.Message);
            Assert.Equal(0, calls);
        }
        Assert.Equal(original, dockerfile.ToString());
    }

    [Theory]
    [InlineData(false, false, "#")]
    [InlineData(false, true, "#")]
    [InlineData(true, false, "#")]
    [InlineData(true, true, "#")]
    [InlineData(false, false, "##")]
    [InlineData(false, true, "##")]
    [InlineData(true, false, "##")]
    [InlineData(true, true, "##")]
    [InlineData(false, false, "%")]
    [InlineData(false, true, "%")]
    [InlineData(true, false, "%")]
    [InlineData(true, true, "%")]
    [InlineData(false, false, "%%")]
    [InlineData(false, true, "%%")]
    [InlineData(true, false, "%%")]
    [InlineData(true, true, "%%")]
    [InlineData(false, false, "/")]
    [InlineData(false, true, "/")]
    [InlineData(true, false, "/")]
    [InlineData(true, true, "/")]
    [InlineData(false, false, "//")]
    [InlineData(false, true, "//")]
    [InlineData(true, false, "//")]
    [InlineData(true, true, "//")]
    public void RawPatternSerializationRefreshesPendingDefaults(
        bool stageScope, bool updateInline, string modifier)
    {
        bool armed = false;
        LiteralToken pending = Literal(new VariableRefToken("MISSING", "?", "stale"));
        VariableRefToken pattern = new("IGNORED", modifier, "placeholder")
        {
            ModifierValueToken = Literal(new MutableSerializedString(() =>
            {
                if (armed) pending.Value = "new";
                return "p";
            }))
        };
        Dockerfile dockerfile = Document(stageScope, pending, pattern,
            new CallbackVariable(() => armed = true));

        if (stageScope && !updateInline)
        {
            VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
                dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
            Assert.Equal("Variable 'MISSING' is not set. Error detail: 'stale'.", error.Message);
        }
        else
        {
            string value = $"new${{IGNORED{modifier}p}}left";
            Assert.Equal(stageScope ? $"ENV RESULT={value}" : $"FROM prefix{value}",
                dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        }
        Assert.True(armed);
        Assert.Equal("new", pending.Value);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, true, true)]
    public void NativeTriviaReferencesRetainPublicRequiredChecks(
        bool stageScope, bool updateInline, bool comment, bool consumerOwner)
    {
        AggregateToken trivia;
        if (comment)
        {
            CommentToken owner = consumerOwner ? new SerializerOnlyComment() : new CommentToken("comment");
            owner.TokenList.Add(new VariableRefToken("MISSING", "?", "required"));
            trivia = owner;
        }
        else
        {
            LineContinuationToken owner = consumerOwner ? new CountingContinuation() : new LineContinuationToken("\n");
            owner.TokenList.Insert(1, new VariableRefToken("MISSING", "?", "required"));
            trivia = owner;
        }
        Dockerfile dockerfile = Document(stageScope, new StringToken("ok"), trivia);
        string original = dockerfile.ToString();

        if (stageScope && !updateInline && !comment)
        {
            Assert.Equal("ENV RESULT=ok", dockerfile.ResolveVariables());
        }
        else
        {
            VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
                dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
            Assert.Equal("Variable 'MISSING' is not set. Error detail: 'required'.", error.Message);
        }
        Assert.Equal(original, dockerfile.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UnrelatedModifierResolutionRetainsPublicQuoteSyntax(bool stageScope, bool updateInline)
    {
        ResolutionOptions options = new() { UpdateInline = updateInline };
        VariableRefToken unrelated = new("UNSET", ":-", "\"kept\"");
        ComputingVariable callback = new((escapeChar, variables, receivedOptions) =>
        {
            Assert.Same(options, receivedOptions);
            return unrelated.ResolveVariables(escapeChar, variables, receivedOptions);
        });
        Dockerfile dockerfile = Document(stageScope, callback);
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=\"kept\"" : "FROM prefix\"kept\"",
            dockerfile.ResolveVariables(options: options));
        if (!updateInline) Assert.Equal(original, dockerfile.ToString());
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
    public void CustomAggregateDescendantsKeepArgModifierSemantics(
        bool stageScope, bool updateInline, bool nested)
    {
        PassthroughAggregate owner = new();
        VariableRefToken variable = new("UNSET", ":-", "\"owned\"");
        owner.TokenList.Add(nested ? Literal(variable) : variable);
        Dockerfile dockerfile = Document(stageScope, owner);
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=owned" : "FROM prefixowned",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(1, owner.ResolutionCount);
        if (!updateInline) Assert.Equal(original, dockerfile.ToString());
    }

    [Fact]
    public void SimpleReferencesDoNotAllocateModifierScopes()
    {
        const int count = 2048;
        Dockerfile dockerfile = Dockerfile.Parse("ARG SET=set\nARG X=" +
            String.Concat(Enumerable.Repeat("$SET", count)) + "\nFROM prefix$X");
        string expected = "FROM prefix" + String.Concat(Enumerable.Repeat("set", count));
        Assert.Equal(expected, dockerfile.ResolveVariables());
        long before = GC.GetAllocatedBytesForCurrentThread();

        string result = dockerfile.ResolveVariables();

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(expected, result);
        Assert.InRange(allocated, 0, 1_500_000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpeculativeQuoteFailuresRemainStickyAfterCallbacks(bool throwOnce)
    {
        bool fail = true;
        InvalidOperationException expected = new("Speculative quote.");
        Dockerfile dockerfile = Document(true, new QuotedString(() =>
        {
            if (!fail) return null;
            if (throwOnce) fail = false;
            throw expected;
        }), new CallbackVariable(() => { }));

        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true })));
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
        private readonly Func<char, IDictionary<string, string?>?, ResolutionOptions?, string?> resolve;

        public ComputingVariable(Func<string> resolve) : this((_, _, _) => resolve()) { }

        public ComputingVariable(Func<char, IDictionary<string, string?>?, ResolutionOptions?, string?> resolve)
            : base("LEFT") => this.resolve = resolve;

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null) => resolve(escapeChar, variables, options);
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

    private sealed class SerializedNewLine : NewLineToken
    {
        private readonly Func<TokenStringOptions, string> serialize;

        public SerializedNewLine(Func<TokenStringOptions, string> serialize) : base("\n") => this.serialize = serialize;

        protected override string GetUnderlyingValue(TokenStringOptions options) => serialize(options);
    }

    private sealed class SerializerOnlyComment : CommentToken
    {
        public SerializerOnlyComment() : base("old") { }
    }

    private sealed class FailingContinuation : LineContinuationToken
    {
        private readonly Func<bool> fail;
        private readonly Exception error;

        public FailingContinuation(Func<bool> fail, Exception error) : base("\n")
        {
            this.fail = fail;
            this.error = error;
        }

        public int FailingSerializationCount { get; private set; }

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            if (fail())
            {
                FailingSerializationCount++;
                throw error;
            }
            return base.GetUnderlyingValue(options);
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

    private sealed class ResolvingContinuation : LineContinuationToken
    {
        private readonly Action resolved;

        public ResolvingContinuation(Action resolved) : base("\n") => this.resolved = resolved;

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            resolved();
            return "custom";
        }
    }

    private sealed class ResolvingComment : CommentToken
    {
        private readonly Action resolved;

        public ResolvingComment(Action resolved) : base("comment") => this.resolved = resolved;

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            resolved();
            return "custom";
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

    private sealed class OptionSerializedString : StringToken
    {
        private readonly Func<TokenStringOptions, string> serialize;

        public OptionSerializedString(Func<TokenStringOptions, string> serialize) : base("placeholder") =>
            this.serialize = serialize;

        protected override string GetUnderlyingValue(TokenStringOptions options) => serialize(options);
    }

    private sealed class OptionsEditingLiteral : LiteralToken
    {
        private readonly Func<ResolutionOptions, string> resolve;

        public OptionsEditingLiteral(Func<ResolutionOptions, string> resolve) : base("placeholder") =>
            this.resolve = resolve;

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null) => resolve(options!);
    }

    private sealed class SerializationQuotedString : StringToken, IQuotableToken
    {
        private readonly Func<bool> active;

        public SerializationQuotedString(Func<bool> active) : base("placeholder") => this.active = active;

        public char? QuoteChar { get; set; }

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            QuoteChar = active() ? '"' : null;
            return active() ? "\\q" : "p";
        }
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
