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
    public void MatchingModifierHandlerIsFoundThroughUnrelatedScopes(int depth)
    {
        Func<LiteralToken, string?> resolver = _ => "owned";
        ResolutionOptions options = new();
        Dictionary<string, string?> variables = new();
        VariableRefToken variable = new("UNSET", ":-", "unused");
        Stack<IDisposable> scopes = new();
        try
        {
            scopes.Push(options.UseModifierValueResolver(variable, '\\', variables, resolver));
            for (int i = 1; i < depth; i++)
            {
                scopes.Push(new ResolutionOptions().UseModifierValueResolver(new VariableRefToken("OTHER"),
                    '\\', variables, _ => "unrelated"));
            }

            Assert.Same(resolver, options.GetModifierValueResolver(variable, '\\', variables));
            Assert.Equal("owned", variable.ResolveVariables('\\', variables, options));
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
    public void ModifierScopesObserveLibraryRemovedMembers(bool nested)
    {
        LiteralToken owner = Literal();
        LiteralToken children = nested ? Literal() : owner;
        if (nested) owner.TokenList.Add(children);
        VariableRefToken variable = new("UNSET", ":-", "\"kept\"");
        children.TokenList.Add(variable);
        ResolutionOptions options = new();
        Dictionary<string, string?> variables = new();

        using (options.UseModifierValueResolver(owner, '\\', variables, _ => "owned"))
        {
            Assert.Equal("owned", variable.ResolveVariables('\\', variables, options));
            children.RemoveResolvedChild(0);
            Assert.Equal("\"kept\"", variable.ResolveVariables('\\', variables, options));
        }
    }

    [Fact]
    public void ModifierScopeOwnershipIgnoresCyclesInOpaqueOwners()
    {
        PassthroughAggregate owner = new();
        owner.TokenList.Add(owner);
        VariableRefToken variable = new("UNSET", ":-", "\"kept\"");
        owner.TokenList.Add(variable);
        VariableRefToken outsider = new("UNSET", ":-", "\"outside\"");
        ResolutionOptions options = new();
        Dictionary<string, string?> variables = new();

        using (options.UseModifierValueResolver(owner, '\\', variables, _ => "owned"))
        {
            Assert.Equal("\"outside\"", outsider.ResolveVariables('\\', variables, options));
            Assert.Equal("owned", variable.ResolveVariables('\\', variables, options));
            owner.RemoveResolvedChild(1);
            Assert.Equal("\"kept\"", variable.ResolveVariables('\\', variables, options));
        }
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

    [Fact]
    public void PendingSuppressedSerializerFailureIsRethrownUnchanged()
    {
        bool fail = false;
        InvalidOperationException expected = new("Suppressed serialization.");
        VariableRefToken suppressed = new("A", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new MutableSerializedString(() =>
            {
                if (!fail) return "placeholder";
                throw expected;
            }))
        };
        Dockerfile dockerfile = Document(false, new StringToken("\\"), suppressed,
            new CallbackVariable(() => fail = true));

        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => dockerfile.ResolveVariables()));
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
    public void NonInlineStagePreservesOriginalModifierTokens()
    {
        CountingContinuation continuation = new();
        VariableRefToken variable = new("UNSET", ":-", "placeholder")
        {
            ModifierValueToken = Literal(continuation, new StringToken("value"))
        };
        Dockerfile dockerfile = Document(true, variable);
        continuation.SerializationCount = 0;

        Assert.Equal("ENV RESULT=value", dockerfile.ResolveVariables());
        Assert.True(continuation.SerializationCount > 0);
        Assert.Same(variable, Assert.Single(dockerfile.Items.OfType<ArgInstruction>()
            .Single().ArgTokens[0].ValueToken!.Tokens));
    }


    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PendingPrimitiveQuoteChangesAreRejected(bool stageScope, bool updateInline)
    {
        bool changed = false;
        Dockerfile dockerfile = Document(stageScope, new StringToken("\\"),
            new QuotedString(() => changed ? '"' : null),
            new VariableRefToken("UNSET", "?", "right"), new CallbackVariable(() => changed = true));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal(ResolutionGuard.StructuralMutationMessage, error.Message);
        Assert.True(changed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PrimitiveQuoteFailureAtCallbackBoundaryIsRethrownUnchanged(bool stageScope, bool updateInline)
    {
        bool callbackEntered = false;
        InvalidOperationException expected = new("Primitive quote.");
        Dockerfile dockerfile = Document(stageScope, new QuotedString(() => throw expected),
            new CallbackVariable(() => callbackEntered = true));

        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline })));
        Assert.False(callbackEntered);
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
    [InlineData(false)]
    [InlineData(true)]
    public void InitialSuppressedSerializerFailurePreservesRightmostError(bool singleQuoted)
    {
        bool failed = false;
        VariableRefToken suppressed = new("IGNORED", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new OptionSerializedString(options =>
            {
                if (options.ExcludeComments) return "placeholder";
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

    [Fact]
    public void InitialSuppressedSerializerFailureIsRethrownUnchanged()
    {
        bool callbackEntered = false;
        FormatException expected = new("Suppressed serialization.");
        VariableRefToken suppressed = new("IGNORED", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new OptionSerializedString(options =>
            {
                if (options.ExcludeComments) return "placeholder";
                throw expected;
            }))
        };
        LiteralToken prefix = Literal(new StringToken("\\"));
        Dockerfile dockerfile = Document(true, prefix, suppressed,
            new CallbackVariable(() => callbackEntered = true));

        Assert.Same(expected, Assert.Throws<FormatException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true })));
        Assert.True(callbackEntered);
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
            }), inner)
        };
        Dockerfile dockerfile = Document(stageScope, outer);

        Assert.Equal(stageScope ? "ENV RESULT=leftab" : "FROM prefixleftab",
            dockerfile.ResolveVariables(options: new ResolutionOptions()));
        Assert.Equal("ARG X=left\"a\\b\"\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        Assert.Equal(":-", inner.Modifier);
        Assert.Equal("a\\\\b", quoted.Value);
        Assert.Equal('"', quoted.QuoteChar);
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

        if (editNewLine)
        {
            Assert.Throws<InvalidOperationException>(() =>
                dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
            Assert.Equal("\n", newLine.Value);
            return;
        }
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
    public void IgnoredTriviaCallbacksRejectSourceEdits(
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

        Assert.Throws<InvalidOperationException>(() => dockerfile.ResolveVariables(options: options));
        Assert.Equal(1, calls);
        Assert.Equal("${MISSING?pending}", pending.ToString());
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
    public void RawPatternSerializationPreservesPendingConsumerDefaults(
        bool stageScope, bool updateInline, string modifier)
    {
        bool armed = false;
        LiteralToken pending = Literal(new MutableSerializedString(() => armed ? "new" : "old"));
        VariableRefToken pattern = new("IGNORED", modifier, "placeholder")
        {
            ModifierValueToken = Literal(new MutableSerializedString(() => "p"))
        };
        Dockerfile dockerfile = Document(stageScope, pending, pattern,
            new CallbackVariable(() => armed = true));

        string value = $"new${{IGNORED{modifier}p}}left";
        Assert.Equal(stageScope ? $"ENV RESULT={value}" : $"FROM prefix{value}",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.True(armed);
        Assert.Equal("new", pending.ToString());
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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SerializedDeclarationMergesUseTheirOwnLayout(bool updateInline, bool targetArg)
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "FROM alpine\nARG SOURCE=value\nARG A=one B=two C=$SOURCE\nENV RESULT=$A/$B/$C");
        ArgInstruction instruction = dockerfile.Items.OfType<ArgInstruction>().Last();
        SerializedLiteral left = new("\"one", "first");
        SerializedLiteral right = new("two\"", "second");
        instruction.ArgTokens[0].ValueToken = left;
        instruction.ArgTokens[1].ValueToken = right;
        string original = dockerfile.ToString();

        string result = targetArg ? dockerfile.ResolveVariables(instruction,
            options: new ResolutionOptions { UpdateInline = updateInline })
            : dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline });

        Assert.Equal(targetArg ? "ARG A=\"one B=two\" C=value\n"
            : updateInline ? "ENV RESULT=first/second/value" : "ENV RESULT=one B=two//value", result);
        Assert.Equal(updateInline ? 1 : 0, left.ResolutionCount);
        Assert.Equal(updateInline ? 1 : 0, right.ResolutionCount);
        if (!updateInline) Assert.Equal(original, dockerfile.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SerializedDeclarationSplitsResolveAllDetachedDefaults(bool updateInline, bool targetArg)
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "FROM alpine\nARG SOURCE=value\nARG A=one B=$SOURCE\nENV RESULT=$A/$EXTRA/$B");
        ArgInstruction instruction = dockerfile.Items.OfType<ArgInstruction>().Last();
        SerializedLiteral literal = new("one EXTRA=$SOURCE", "custom");
        instruction.ArgTokens[0].ValueToken = literal;
        string original = dockerfile.ToString();

        string result = targetArg ? dockerfile.ResolveVariables(instruction,
            options: new ResolutionOptions { UpdateInline = updateInline })
            : dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline });

        Assert.Equal(targetArg ? $"ARG A=one EXTRA={(updateInline ? "$SOURCE" : "value")} B=value\n"
            : updateInline ? "ENV RESULT=custom//value" : "ENV RESULT=one/value/value", result);
        Assert.Equal(updateInline ? 1 : 0, literal.ResolutionCount);
        if (!updateInline) Assert.Equal(original, dockerfile.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SerializedDeclarationLayoutDoesNotDependOnCountAlone(bool updateInline, bool targetArg)
    {
        Dockerfile document = Dockerfile.Parse(
            "FROM alpine\nARG SOURCE=value\nARG A=one B=two C=$SOURCE\nENV RESULT=$A/$B/$EXTRA/$C");
        ArgInstruction instruction = document.Items.OfType<ArgInstruction>().Last();
        instruction.ArgTokens[0].ValueToken = new SerializedLiteral("\"one", "first");
        instruction.ArgTokens[1].ValueToken = new SerializedLiteral("two\" EXTRA=$SOURCE", "second");
        string original = document.ToString();

        string result = targetArg ? document.ResolveVariables(instruction,
            options: new ResolutionOptions { UpdateInline = updateInline })
            : document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline });

        Assert.Equal(targetArg
            ? $"ARG A=\"one B=two\" EXTRA={(updateInline ? "$SOURCE" : "value")} C=value\n"
            : updateInline ? "ENV RESULT=first/second//value" : "ENV RESULT=one B=two//value/value", result);
        if (!updateInline) Assert.Equal(original, document.ToString());
    }

    [Theory]
    [InlineData(null, "ARG A=\"one B=two\" EXTRA= C=fallback\n")]
    [InlineData("override", "ARG A=\"one B=two\" EXTRA= C=override\n")]
    public void SerializedDeclarationLayoutRetainsOverridesAndUnsetBindings(string? overrideValue, string expected)
    {
        Dockerfile document = Dockerfile.Parse(
            "ARG GLOBAL=global\nFROM alpine\nARG GLOBAL\nARG SOURCE=old\n" +
            "ARG A=one C=${SOURCE-fallback}\nENV RESULT=$C");
        ArgInstruction instruction = document.Items.OfType<ArgInstruction>().Last();
        instruction.ArgTokens[0].ValueToken = new SerializedLiteral("\"one B=two\" EXTRA=${MISSING:+unused}",
            "custom");
        string original = document.ToString();
        Dictionary<string, string?> overrides = new() { ["SOURCE"] = overrideValue };

        Assert.Equal(expected, document.ResolveVariables(instruction, overrides));
        Assert.Equal(original, document.ToString());
        Assert.Equal(overrideValue, overrides["SOURCE"]);
    }

    private sealed class SerializedLiteral : LiteralToken
    {
        private readonly string syntax;
        private readonly string result;
        private readonly Action<IDictionary<string, string?>?>? beforeResolution;

        public SerializedLiteral(string syntax, string result,
            Action<IDictionary<string, string?>?>? beforeResolution = null) : base("placeholder")
        {
            this.syntax = syntax;
            this.result = result;
            this.beforeResolution = beforeResolution;
        }

        public int ResolutionCount { get; private set; }

        protected override string GetUnderlyingValue(TokenStringOptions options) => syntax;

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            ResolutionCount++;
            beforeResolution?.Invoke(variables);
            return result;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SerializedDeclarationBoundariesCanChangeWithoutChangingNames(bool updateInline, bool targetArg)
    {
        Dockerfile document = Dockerfile.Parse("FROM alpine\nARG A=one A=two\nENV RESULT=$A");
        ArgInstruction instruction = document.Items.OfType<ArgInstruction>().Single();
        SerializedLiteral left = new("\"one", "first");
        SerializedLiteral right = new("two\" A=three", "second");
        instruction.ArgTokens[0].ValueToken = left;
        instruction.ArgTokens[1].ValueToken = right;
        string original = document.ToString();

        string result = targetArg ? document.ResolveVariables(instruction,
            options: new ResolutionOptions { UpdateInline = updateInline })
            : document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline });

        Assert.Equal(targetArg ? "ARG A=\"one A=two\" A=three\n"
            : updateInline ? "ENV RESULT=second" : "ENV RESULT=three", result);
        Assert.Equal(updateInline ? 1 : 0, left.ResolutionCount);
        Assert.Equal(updateInline ? 1 : 0, right.ResolutionCount);
        if (!updateInline) Assert.Equal(original, document.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclarationLayoutDoesNotReadLaterNamesBeforeRequiredDefaults(bool updateInline)
    {
        Dockerfile document = Dockerfile.Parse("FROM alpine\nARG A=${MISSING?first} B=two\nENV RESULT=$A");
        ArgInstruction instruction = document.Items.OfType<ArgInstruction>().Single();
        CallbackName name = new(() => throw new InvalidOperationException("Later name."));
        instruction.ArgTokens[1].NameToken = name;
        string original = document.ToString();

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal("Variable 'MISSING' is not set. Error detail: 'first'.", error.Message);
        Assert.Equal(0, name.ValueReadCount);
        Assert.Equal(original, document.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclarationNamesRemainLazyAfterEarlierCallbacks(bool updateInline)
    {
        Dockerfile document = Dockerfile.Parse("FROM alpine\nARG A=one B=two\nENV RESULT=$A/$B/$C");
        ArgInstruction instruction = document.Items.OfType<ArgInstruction>().Single();
        bool resolvedFirst = false;
        CallbackName name = new(() => Assert.True(resolvedFirst));
        instruction.ArgTokens[1].NameToken = name;
        instruction.ArgTokens[0].ValueToken = new SerializedLiteral("one", "first", _ =>
        {
            resolvedFirst = true;
        });

        Assert.Equal("ENV RESULT=first/two/", document.ResolveVariables(
            options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("B", name.Value);
    }

    private sealed class CallbackName : Variable
    {
        private readonly Action beforeValueRead;

        public CallbackName(Action beforeValueRead) : base("B") => this.beforeValueRead = beforeValueRead;

        public int ValueReadCount { get; private set; }

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            if (options.ExcludeQuotes)
            {
                ValueReadCount++;
                beforeValueRead();
            }
            return base.GetUnderlyingValue(options);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HelperDeclarationsDoNotChangeTheInstructionLayout(bool updateInline, bool splitDeclaration)
    {
        Dockerfile document = Dockerfile.Parse("FROM alpine\nARG X=placeholder\nENV RESULT=$X/$Y");
        HelperDeclarationLiteral literal = new(splitDeclaration);
        document.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken = literal;
        string original = document.ToString();

        Assert.Equal(!updateInline && splitDeclaration ? "ENV RESULT=A=one/two" : "ENV RESULT=computed/",
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(updateInline || !splitDeclaration ? 1 : 0, literal.ResolutionCount);
        if (!updateInline) Assert.Equal(original, document.ToString());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void CustomInstructionSerializationUsesParsedDefaults(
        bool updateInline, bool splitDeclaration, bool workerSerialization)
    {
        CopyingOptionsArgInstruction instruction = new(workerSerialization);
        SerializedLiteral literal = new(splitDeclaration ? "one Y=two" : "one", "computed");
        instruction.ArgTokens[0].ValueToken = literal;
        Dockerfile document = new(new DockerfileConstruct[]
        {
            FromInstruction.Parse("FROM alpine\n"), instruction, EnvInstruction.Parse("ENV RESULT=$X/$Y")
        });
        string original = document.ToString();

        Assert.Equal(updateInline ? "ENV RESULT=computed/"
            : splitDeclaration ? "ENV RESULT=one/two" : "ENV RESULT=one/",
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(updateInline ? 1 : 0, literal.ResolutionCount);
        if (!updateInline) Assert.Equal(original, document.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CachedCustomInstructionsUseParsedDefaults(bool updateInline, bool splitDeclaration)
    {
        UnobservedArgInstruction instruction = new(splitDeclaration);
        SerializedLiteral literal = new("one", "computed");
        instruction.ArgTokens[0].ValueToken = literal;
        Dockerfile document = new(new DockerfileConstruct[]
        {
            FromInstruction.Parse("FROM alpine\n"), instruction, EnvInstruction.Parse("ENV RESULT=$X/$Y")
        });
        string original = document.ToString();

        Assert.Equal(updateInline ? "ENV RESULT=computed/"
            : splitDeclaration ? "ENV RESULT=one/two" : "ENV RESULT=one/",
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(updateInline ? 1 : 0, literal.ResolutionCount);
        if (!updateInline) Assert.Equal(original, document.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NonInlineNamesFollowSerializedProjection(bool updateInline, bool overrideName)
    {
        Dockerfile document = Dockerfile.Parse("FROM alpine\nARG A=one\nENV RESULT=$A/$B");
        document.Items.OfType<ArgInstruction>().Single().ArgTokens[0].NameToken = new FilteredDeclarationName();
        string original = document.ToString();
        Dictionary<string, string?> overrides = new();
        if (overrideName) overrides["A"] = "override";

        Assert.Equal(updateInline ? "ENV RESULT=/one" : overrideName ? "ENV RESULT=override/" : "ENV RESULT=one/",
            document.ResolveVariables(overrides, new ResolutionOptions { UpdateInline = updateInline }));
        if (!updateInline) Assert.Equal(original, document.ToString());
    }

    [Fact]
    public void NameProjectionsAreStableAcrossRepeatedSerialization()
    {
        Dockerfile document = Dockerfile.Parse("FROM alpine\nARG A=one\nENV RESULT=$A/$B");
        document.Items.OfType<ArgInstruction>().Single().ArgTokens[0].NameToken = new FilteredDeclarationName();

        Assert.Equal("ENV RESULT=one/", document.ResolveVariables());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EarlierCallbacksCannotReplaceOrEditLaterNameSyntax(bool updateInline, bool replaceName)
    {
        Dockerfile document = Dockerfile.Parse("FROM alpine\nARG X=one B=two\nENV RESULT=$X/$B/$C");
        ArgInstruction instruction = document.Items.OfType<ArgInstruction>().Single();
        instruction.ArgTokens[0].ValueToken = new SerializedLiteral("one", "computed", _ =>
        {
            if (replaceName) instruction.ArgTokens[1].NameToken = new Variable("C");
            else Assert.IsType<StringToken>(Assert.Single(instruction.ArgTokens[1].NameToken.Tokens)).Value = "C";
        });

        Assert.Throws<InvalidOperationException>(() => document.ResolveVariables(
            options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("B", instruction.ArgTokens[1].Name);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EarlierCallbacksCannotEditNestedNameSyntax(bool updateInline, bool replaceChild)
    {
        Dockerfile document = Dockerfile.Parse("FROM alpine\nARG X=one B=two\nENV RESULT=$X/$B/$C");
        ArgInstruction instruction = document.Items.OfType<ArgInstruction>().Single();
        NestedName name = new();
        instruction.ArgTokens[1].NameToken = name;
        instruction.ArgTokens[0].ValueToken = new SerializedLiteral("one", "computed", _ =>
        {
            if (replaceChild) ((IList<Token>)name.Inner.Tokens)[0] = new StringToken("C");
            else Assert.IsType<StringToken>(Assert.Single(name.Inner.Tokens)).Value = "C";
        });

        Assert.Throws<InvalidOperationException>(() => document.ResolveVariables(
            options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("B", name.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingObservationsStillDetectNativeBoundaryChanges(bool updateInline)
    {
        CachedLayoutArgInstruction instruction = new();
        Dockerfile document = new(new DockerfileConstruct[]
        {
            FromInstruction.Parse("FROM alpine\n"), instruction, EnvInstruction.Parse("ENV RESULT=$A")
        });
        string original = document.ToString();

        Assert.Equal(updateInline ? "ENV RESULT=two" : "ENV RESULT=three",
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        if (!updateInline) Assert.Equal(original, document.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MissingObservationsStillDetectCustomBoundaryChanges(bool updateInline, bool changesBoundary)
    {
        CachedLayoutArgInstruction instruction = new(changesBoundary);
        instruction.ArgTokens[0].ValueToken = new SerializedLiteral("one", "first");
        instruction.ArgTokens[1].ValueToken = new SerializedLiteral("two", "second");
        Dockerfile document = new(new DockerfileConstruct[]
        {
            FromInstruction.Parse("FROM alpine\n"), instruction, EnvInstruction.Parse("ENV RESULT=$A")
        });
        string original = document.ToString();

        Assert.Equal(updateInline ? "ENV RESULT=second"
            : changesBoundary ? "ENV RESULT=three" : "ENV RESULT=two",
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        if (!updateInline) Assert.Equal(original, document.ToString());
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
    public void EarlierCallbacksCanChangeNameSerializerState(bool updateInline, bool rename, bool projected)
    {
        bool changed = false;
        Dockerfile document = Dockerfile.Parse("FROM alpine\nARG X=one B=two\nENV RESULT=$X/$A/$B/$C");
        ArgInstruction instruction = document.Items.OfType<ArgInstruction>().Single();
        instruction.ArgTokens[1].NameToken = new StateDependentName(options =>
            changed ? "C" : projected && !options.ExcludeQuotes ? "A" : "B");
        instruction.ArgTokens[0].ValueToken = new SerializedLiteral("one", "computed", _ => changed = rename);

        string expected = rename ? "ENV RESULT=computed///two"
            : !updateInline && projected ? "ENV RESULT=computed/two//" : "ENV RESULT=computed//two/";
        Assert.Equal(expected, document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(rename, changed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonInlineStageBindingsRetainConsumedNamesAcrossCallbacks(bool overrideValue)
    {
        NameChangingDictionary variables = new(overrideValue) { ["NAME"] = "A" };
        if (overrideValue) variables["A"] = "override";
        Dockerfile document = Dockerfile.Parse("FROM alpine\nARG A=before\nENV RESULT=$A/$B");
        ArgDeclaration declaration = document.Items.OfType<ArgInstruction>().Single().ArgTokens[0];
        declaration.NameToken = new StateDependentName(_ => variables["NAME"]!);
        if (!overrideValue)
        {
            declaration.ValueToken = new SerializedLiteral("before", "computed", _ => variables["NAME"] = "B");
        }
        Token[] originalNameTokens = declaration.NameToken.Tokens.ToArray();
        Token[] originalDefaultTokens = declaration.ValueToken!.Tokens.ToArray();

        Assert.Equal(overrideValue ? "ENV RESULT=override/" : "ENV RESULT=computed/",
            document.ResolveVariables(variables));
        Assert.Equal("B", variables["NAME"]);
        Assert.Equal(originalNameTokens, declaration.NameToken.Tokens);
        Assert.Equal(originalDefaultTokens, declaration.ValueToken.Tokens);
    }

    [Fact]
    public void NonInlineBareDeclarationsImportTheConsumedGlobalName()
    {
        NameChangingDictionary variables = new(true, skipLookups: 1) { ["NAME"] = "A" };
        Dockerfile document = Dockerfile.Parse(
            "ARG A=first\nARG B=second\nFROM alpine\nARG A\nENV RESULT=$A/$B");
        ArgDeclaration declaration = document.Items.OfType<ArgInstruction>().Last().ArgTokens[0];
        declaration.NameToken = new StateDependentName(_ => variables["NAME"]!);

        Assert.Equal("ENV RESULT=first/", document.ResolveVariables(variables));
        Assert.Equal("B", variables["NAME"]);
    }

    [Fact]
    public void NonInlineNameCaptureHandlesDeepOpaqueBackingTrees()
    {
        Token child = new StringToken("hidden");
        for (int i = 0; i < 30_000; i++)
        {
            child = new LiteralToken(new[] { child }, true, '\\');
        }
        StateDependentName name = new(_ => "A");
        IList<Token> children = Assert.IsAssignableFrom<IList<Token>>(name.Tokens);
        children.Add(child);
        children.Add(child);
        Dockerfile document = Dockerfile.Parse("FROM alpine\nARG A=value\nENV RESULT=$A");
        document.Items.OfType<ArgInstruction>().Single().ArgTokens[0].NameToken = name;

        Assert.Equal("ENV RESULT=value", document.ResolveVariables());
        Assert.Equal("A", name.Value);
        Assert.Same(child, children[1]);
        Assert.Same(child, children[2]);
    }

    private sealed class NameChangingDictionary(bool changeDuringLookup, int skipLookups = 0)
        : Dictionary<string, string?>, IDictionary<string, string?>
    {
        public new bool TryGetValue(string key, out string? value)
        {
            if (changeDuringLookup && key == "A" && skipLookups-- == 0) this["NAME"] = "B";
            return base.TryGetValue(key, out value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectedNamesRetainBaselineDefaultResolution(bool updateInline)
    {
        Dockerfile document = Dockerfile.Parse("FROM alpine\nARG A=placeholder\nENV RESULT=$A/$B");
        ArgDeclaration declaration = document.Items.OfType<ArgInstruction>().Single().ArgTokens[0];
        declaration.NameToken = new FilteredDeclarationName();
        SerializedLiteral value = new("placeholder", "computed");
        declaration.ValueToken = value;

        Assert.Equal(updateInline ? "ENV RESULT=/computed" : "ENV RESULT=placeholder/",
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(updateInline ? 1 : 0, value.ResolutionCount);
    }


    [Fact]
    public void NativeDefaultsPreserveLaterNameProjections()
    {
        Dockerfile document = Dockerfile.Parse("FROM alpine\nARG X=one A=two\nENV RESULT=$X/$A/$B");
        document.Items.OfType<ArgInstruction>().Single().ArgTokens[1].NameToken = new FilteredDeclarationName();

        Assert.Equal("ENV RESULT=one/two/", document.ResolveVariables());
    }

    private sealed class StateDependentName : Variable
    {
        private readonly Func<TokenStringOptions, string> serialize;

        public StateDependentName(Func<TokenStringOptions, string> serialize) : base("B") =>
            this.serialize = serialize;

        protected override string GetUnderlyingValue(TokenStringOptions options) => serialize(options);
    }




    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssignmentLayoutMismatchDoesNotSerializeUnusedSourceNames(bool laterMismatch)
    {
        CachedAssignedArgInstruction instruction = new(laterMismatch);
        instruction.ArgTokens[0].NameToken = new StateDependentName(_ =>
            throw new InvalidOperationException("Unused source name."));
        Dockerfile document = new(new DockerfileConstruct[]
        {
            FromInstruction.Parse("FROM alpine\n"), instruction, EnvInstruction.Parse("ENV RESULT=$X")
        });

        Assert.Equal("ENV RESULT=value", document.ResolveVariables());
    }

    [Fact]
    public void CustomInstructionSerializationDoesNotReadUnusedOriginalDeclarations()
    {
        PureAuthoritativeArgInstruction instruction = new();
        instruction.ArgTokens[0].NameToken = new StateDependentName(_ =>
            throw new InvalidOperationException("Unused source name."));
        Dockerfile document = new(new DockerfileConstruct[]
        {
            FromInstruction.Parse("FROM alpine\n"), instruction, EnvInstruction.Parse("ENV RESULT=$A/$B")
        });

        Assert.Equal("ENV RESULT=one/two", document.ResolveVariables());
    }

    private sealed class PureAuthoritativeArgInstruction : ArgInstruction
    {
        public PureAuthoritativeArgInstruction() : base("A", "unused") =>
            TokenList.AddRange(new Token[] { new WhitespaceToken(" "), new ArgDeclaration("B", "three") });

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            ArgTokens[1].ToString(options);
            return "ARG A=one B=two\n";
        }
    }


    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DiscardedMatchingObservationsDoNotProveDeclarationBoundaries(bool updateInline, bool changesBoundary)
    {
        MisleadingHelperArgInstruction instruction = new(changesBoundary);
        Dockerfile document = new(new DockerfileConstruct[]
        {
            FromInstruction.Parse("FROM alpine\n"), instruction, EnvInstruction.Parse("ENV RESULT=$A")
        });
        string original = document.ToString();

        Assert.Equal(updateInline ? "ENV RESULT=second"
            : changesBoundary ? "ENV RESULT=three" : "ENV RESULT=two",
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        if (!updateInline) Assert.Equal(original, document.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TransferredEnvLiteralsRetainTheirExistingOpaqueResolution(bool stageScope, bool updateInline)
    {
        LiteralToken value = EnvInstruction.Parse("ENV X=\"a\\\"b\"").VariableTokens[0].ValueToken!;
        Dockerfile document = Document(stageScope);
        document.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken = value;
        string original = document.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=\"a\\\"b\"" : "FROM prefix\"a\\\"b\"",
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        if (!updateInline) Assert.Equal(original, document.ToString());
    }

    private sealed class CachedAssignedArgInstruction : ArgInstruction
    {
        private readonly bool laterMismatch;

        public CachedAssignedArgInstruction(bool laterMismatch) : base("X", laterMismatch ? "value" : null)
        {
            this.laterMismatch = laterMismatch;
            if (laterMismatch)
                TokenList.AddRange(new Token[] { new WhitespaceToken(" "), new ArgDeclaration("Y") });
        }

        protected override string GetUnderlyingValue(TokenStringOptions options) =>
            laterMismatch ? "ARG X=value Y=later\n" : "ARG X=value\n";
    }

    private sealed class ProjectionEditingName : Variable
    {
        private readonly Func<bool> edit;
        private readonly bool keepProjection;

        public ProjectionEditingName(Func<bool> edit, bool keepProjection) : base("B")
        {
            this.edit = edit;
            this.keepProjection = keepProjection;
        }

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            if (options.ExcludeQuotes) return base.GetUnderlyingValue(options);
            if (!edit()) return "A";
            Value = "C";
            return keepProjection ? "A" : "C";
        }
    }

    private sealed class MisleadingHelperArgInstruction : ArgInstruction
    {
        private bool helper;

        public MisleadingHelperArgInstruction(bool changesBoundary) : base("A", "one")
        {
            TokenList.AddRange(new Token[] { new WhitespaceToken(" "), new ArgDeclaration("A", "two") });
            ArgTokens[0].ValueToken = new SwitchingLiteral(
                () => helper ? "\"one A=two\"" : changesBoundary ? "\"one" : "one", "first");
            ArgTokens[1].ValueToken = new SwitchingLiteral(
                () => helper ? "three" : changesBoundary ? "two\" A=three" : "two", "second");
        }

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            string first = ArgTokens[0].ToString(options);
            string second = ArgTokens[1].ToString(options);
            helper = true;
            try
            {
                ArgTokens[0].ToString(options);
                ArgTokens[1].ToString(options);
            }
            finally
            {
                helper = false;
            }
            return $"ARG {first} {second}\n";
        }
    }

    private sealed class SwitchingLiteral : LiteralToken
    {
        private readonly Func<string> serialize;
        private readonly string value;

        public SwitchingLiteral(Func<string> serialize, string value) : base("placeholder")
        {
            this.serialize = serialize;
            this.value = value;
        }

        protected override string GetUnderlyingValue(TokenStringOptions options) => serialize();

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null) => value;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AuthoritativeHelperObservationsDoNotHideBoundaryChanges(bool updateInline, bool changesBoundary)
    {
        Dockerfile document = Dockerfile.Parse(changesBoundary
            ? "FROM alpine\nARG A=one A=two\nENV RESULT=$A"
            : "FROM alpine\nARG A=one B=two\nENV RESULT=$A/$B");
        ArgInstruction instruction = document.Items.OfType<ArgInstruction>().Single();
        instruction.ArgTokens[0].ValueToken = new SerializedLiteral(changesBoundary ? "\"one" : "one", "first");
        instruction.ArgTokens[1].ValueToken = new AuthoritativeHelperLiteral(instruction.ArgTokens[0],
            changesBoundary ? "two\" A=three" : "two");
        string original = document.ToString();

        Assert.Equal(changesBoundary ? updateInline ? "ENV RESULT=second" : "ENV RESULT=three"
            : "ENV RESULT=first/second",
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        if (!updateInline) Assert.Equal(original, document.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void WorkerCompletionOrderDoesNotChangeDeclarationLayout(bool updateInline, bool concurrent)
    {
        OrderedWorkerArgInstruction instruction = new(concurrent);
        SerializedLiteral first = new("one", "first");
        SerializedLiteral second = new("two", "second");
        instruction.ArgTokens[0].ValueToken = first;
        instruction.ArgTokens[1].ValueToken = second;
        Dockerfile document = new(new DockerfileConstruct[]
        {
            FromInstruction.Parse("FROM alpine\n"), instruction, EnvInstruction.Parse("ENV RESULT=$X/$Y")
        });
        string original = document.ToString();

        Assert.Equal(updateInline ? "ENV RESULT=first/second" : "ENV RESULT=one/two",
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(updateInline ? 1 : 0, first.ResolutionCount);
        Assert.Equal(updateInline ? 1 : 0, second.ResolutionCount);
        if (!updateInline) Assert.Equal(original, document.ToString());
    }

    [Theory]
    [InlineData(false, 16)]
    [InlineData(false, 128)]
    [InlineData(true, 16)]
    [InlineData(true, 128)]
    public void ConcurrentSerializationCapturesEveryDeclaration(bool updateInline, int count)
    {
        ParallelArgInstruction instruction = new(count);
        SerializedLiteral[] values = instruction.ArgTokens.Select((declaration, i) =>
        {
            SerializedLiteral value = new("one", $"value{i}");
            declaration.ValueToken = value;
            return value;
        }).ToArray();
        Dockerfile document = new(new DockerfileConstruct[]
        {
            FromInstruction.Parse("FROM alpine\n"), instruction,
            EnvInstruction.Parse($"ENV RESULT=$X0/$X{count - 1}")
        });
        string original = document.ToString();

        Assert.Equal(updateInline ? $"ENV RESULT=value0/value{count - 1}" : "ENV RESULT=one/one",
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.All(values, value => Assert.Equal(updateInline ? 1 : 0, value.ResolutionCount));
        if (!updateInline) Assert.Equal(original, document.ToString());
    }


    [Fact]
    public void DeferredTriviaOwnerQuoteFailuresKeepTheirIdentity()
    {
        bool fail = false;
        InvalidOperationException expected = new("Owner quote.");
        QuotedTriviaComment owner = new(() =>
        {
            if (!fail) return null;
            throw expected;
        }, new CallbackVariable(() => fail = true));
        Dockerfile document = Document(false, owner);

        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => document.ResolveVariables()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnchangedOpaqueReferencesResolveOnce(bool updateInline)
    {
        const int count = 4096;
        int calls = 0;
        Dockerfile document = Document(false, Enumerable.Range(0, count)
            .Select(_ => (Token)new ComputingVariable(() =>
            {
                calls++;
                return "x";
            })).ToArray());
        string original = document.ToString();

        Assert.Equal("FROM prefix" + new string('x', count),
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(count, calls);
        if (!updateInline) Assert.Equal(original, document.ToString());
    }


    [Theory]
    [InlineData("constant")]
    [InlineData("\"quoted\"")]
    public void NativeConstantCopiesPreserveSyntaxWithoutMutation(string syntax)
    {
        Dockerfile document = Dockerfile.Parse($"FROM alpine\nARG X={syntax}\nENV RESULT=$X");
        ArgInstruction instruction = document.Items.OfType<ArgInstruction>().Single();
        Assert.Equal($"ARG X={syntax}\n", document.ResolveVariables(instruction));
        Assert.Equal($"FROM alpine\nARG X={syntax}\nENV RESULT=$X", document.ToString());
    }

    private sealed class NestedName : Variable
    {
        public NestedName() : base("B") => ReplaceWithToken(Inner);

        public LiteralToken Inner { get; } = new("B");
    }

    private sealed class CachedLayoutArgInstruction : ArgInstruction
    {
        private readonly bool changesBoundary;

        public CachedLayoutArgInstruction(bool changesBoundary = true) : base("A", "one")
        {
            this.changesBoundary = changesBoundary;
            TokenList.AddRange(new Token[] { new WhitespaceToken(" "), new ArgDeclaration("A", "two") });
        }

        protected override string GetUnderlyingValue(TokenStringOptions options) =>
            changesBoundary ? "ARG A=\"one A=two\" A=three\n" : "ARG A=one A=two\n";
    }

    private sealed class AuthoritativeHelperLiteral : LiteralToken
    {
        private readonly ArgDeclaration helper;
        private readonly string syntax;

        public AuthoritativeHelperLiteral(ArgDeclaration helper, string syntax) : base("placeholder")
        {
            this.helper = helper;
            this.syntax = syntax;
        }

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            helper.ToString(options);
            return syntax;
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null) => "second";
    }

    private sealed class OrderedWorkerArgInstruction : ArgInstruction
    {
        private readonly bool concurrent;

        public OrderedWorkerArgInstruction(bool concurrent) : base("X", "one")
        {
            this.concurrent = concurrent;
            TokenList.AddRange(new Token[]
            {
                new WhitespaceToken(" "), new ArgDeclaration("Y", "two"), new NewLineToken("\n")
            });
        }

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            if (!concurrent)
            {
                string second = Task.Run(() => ArgTokens[1].ToString(options)).GetAwaiter().GetResult();
                string first = Task.Run(() => ArgTokens[0].ToString(options)).GetAwaiter().GetResult();
                return $"ARG {first} {second}\n";
            }
            TaskCompletionSource renderedSecond = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<string> firstTask = Task.Run(async () =>
            {
                await renderedSecond.Task;
                return ArgTokens[0].ToString(options);
            });
            Task<string> secondTask = Task.Run(() =>
            {
                try { return ArgTokens[1].ToString(options); }
                finally { renderedSecond.SetResult(); }
            });
            string[] results = Task.WhenAll(firstTask, secondTask).GetAwaiter().GetResult();
            return $"ARG {results[0]} {results[1]}\n";
        }
    }

    private sealed class ParallelArgInstruction : ArgInstruction
    {
        public ParallelArgInstruction(int count) : base(Enumerable.Range(0, count)
            .ToDictionary(i => $"X{i}", _ => (string?)"one")) { }

        protected override string GetUnderlyingValue(TokenStringOptions options) =>
            "ARG " + String.Join(" ", Task.WhenAll(ArgTokens.Select(declaration =>
                Task.Run(() => declaration.ToString(options)))).GetAwaiter().GetResult()) + "\n";
    }

    private sealed class QuotedTriviaComment : CommentToken, IQuotableToken
    {
        private readonly Func<char?> getQuote;

        public QuotedTriviaComment(Func<char?> getQuote, params Token[] tokens) : base("comment")
        {
            this.getQuote = getQuote;
            ReplaceWithTokens(tokens);
        }

        public char? QuoteChar { get => getQuote(); set { } }
    }

    private sealed class HelperDeclarationLiteral : LiteralToken
    {
        private readonly ArgDeclaration helper = new("A", "one");
        private readonly bool splitDeclaration;

        public HelperDeclarationLiteral(bool splitDeclaration) : base("placeholder") =>
            this.splitDeclaration = splitDeclaration;

        public int ResolutionCount { get; private set; }

        protected override string GetUnderlyingValue(TokenStringOptions options) =>
            helper.ToString(options) + (splitDeclaration ? " Y=two" : String.Empty);

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            ResolutionCount++;
            return "computed";
        }
    }

    private sealed class CopyingOptionsArgInstruction : ArgInstruction
    {
        private readonly bool workerSerialization;

        public CopyingOptionsArgInstruction(bool workerSerialization) : base("X", "one")
        {
            this.workerSerialization = workerSerialization;
            TokenList.Add(new NewLineToken("\n"));
        }

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            TokenStringOptions copied = new(options.ExcludeLineContinuations, options.ExcludeQuotes,
                options.ExcludeComments, options.ExcludeNewLines);
            return workerSerialization ? Task.Run(() => base.GetUnderlyingValue(copied)).GetAwaiter().GetResult() :
                base.GetUnderlyingValue(copied);
        }
    }

    private sealed class UnobservedArgInstruction : ArgInstruction
    {
        private readonly bool splitDeclaration;

        public UnobservedArgInstruction(bool splitDeclaration) : base("X", "one") =>
            this.splitDeclaration = splitDeclaration;

        protected override string GetUnderlyingValue(TokenStringOptions options) =>
            splitDeclaration ? "ARG X=one Y=two\n" : "ARG X=one\n";
    }

    private sealed class FilteredDeclarationName : Variable
    {
        public FilteredDeclarationName() : base("A") { }

        protected override string GetUnderlyingValue(TokenStringOptions options) => options.ExcludeQuotes ? "B" : "A";
    }

    [Theory]
    [InlineData(false, "one ")]
    [InlineData(false, "one\t")]
    [InlineData(true, "one ")]
    [InlineData(true, "one\t")]
    public void DeclarationTrailingWhitespaceDoesNotDiscardCustomResolvers(bool updateInline, string syntax)
    {
        Dockerfile document = Dockerfile.Parse("FROM alpine\nARG A=one B=two\nENV RESULT=$A/$B");
        SerializedLiteral value = new(syntax, "custom");
        document.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken = value;
        string original = document.ToString();

        Assert.Equal("ENV RESULT=custom/two", document.ResolveVariables(
            options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(1, value.ResolutionCount);
        if (!updateInline) Assert.Equal(original, document.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedInheritedScopesDoNotTrackTraversal(bool originalContext)
    {
        ResolutionOptions options = new();
        Dictionary<string, string?> variables = new();
        LiteralToken owner = Literal(new VariableRefToken("UNSET"));
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker;
        using (options.UseModifierValueResolver(owner, '\\', variables, _ => "owned"))
        {
            worker = Task.Run(async () =>
            {
                await gate.Task;
                ResolutionOptions requestedOptions = originalContext ? options : new();
                Dictionary<string, string?> requestedVariables = originalContext ? variables : new();
                using var traversal = requestedOptions.TrackModifierResolutionTraversal(owner, '\\', requestedVariables);
                Assert.Null(traversal);
                Assert.Equal(String.Empty, owner.ResolveVariables('\\', requestedVariables, requestedOptions));
            });
        }
        gate.SetResult();
        await worker.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void UnrelatedModifierContextsDoNotTrackTraversal(bool differentOptions,
        bool differentVariables, bool differentEscape)
    {
        ResolutionOptions options = new();
        Dictionary<string, string?> variables = new();
        LiteralToken owner = Literal(new VariableRefToken("UNSET"));
        using (options.UseModifierValueResolver(owner, '\\', variables, _ => "owned"))
        {
            ResolutionOptions requestedOptions = differentOptions ? new() : options;
            Dictionary<string, string?> requestedVariables = differentVariables ? new() : variables;
            using var traversal = requestedOptions.TrackModifierResolutionTraversal(owner,
                differentEscape ? '`' : '\\', requestedVariables);
            Assert.Null(traversal);
        }
    }

    [Fact]
    public async Task TraversalCanUseActiveParentOfDisposedInheritedScope()
    {
        ResolutionOptions options = new();
        Dictionary<string, string?> variables = new();
        LiteralToken owner = Literal(new VariableRefToken("UNSET", ":-", "fallback"));
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (options.UseModifierValueResolver(owner, '\\', variables, _ => "owned"))
        {
            Task worker;
            using (new ResolutionOptions().UseModifierValueResolver(Literal(), '\\',
                new Dictionary<string, string?>(), _ => "unrelated"))
            {
                worker = Task.Run(async () =>
                {
                    await gate.Task;
                    using var traversal = options.TrackModifierResolutionTraversal(owner, '\\', variables);
                    Assert.NotNull(traversal);
                    Assert.Equal("owned", owner.ResolveVariables('\\', variables, options));
                });
            }
            gate.SetResult();
            await worker.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ModifierOwnershipChecksAllocateLinearly(bool updateInline, bool nested)
    {
        long Measure(int count)
        {
            PassthroughAggregate owner = new();
            for (int i = 0; i < count; i++)
            {
                VariableRefToken variable = new("UNSET", ":-", "x");
                owner.TokenList.Add(nested ? Literal(variable) : variable);
            }
            Dockerfile document = Document(false, owner);
            string expected = "FROM prefix" + new string('x', count);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(expected, document.ResolveVariables(
                options: new ResolutionOptions { UpdateInline = updateInline }));
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Measure(16);
        long small = Measure(128);
        long large = Measure(512);

        Assert.InRange(large, 0, 4 * small + 500_000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectModifierForwardingChecksAllocateLinearly(bool updateInline)
    {
        long Measure(int count)
        {
            DirectForwardingAggregate owner = new(Enumerable.Range(0, count)
                .Select(_ => new VariableRefToken("UNSET", ":-", "x")));
            Dockerfile document = Document(false, owner);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal("FROM prefix" + new string('x', count),
                document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Measure(16);
        long small = Measure(128);
        long large = Measure(512);

        Assert.InRange(large, 0, 4 * small + 500_000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedModifierOwnershipRechecksAliasesAfterLibraryPublication(bool aliasRemains)
    {
        ResolutionOptions options = new();
        Dictionary<string, string?> variables = new();
        VariableRefToken variable = new("UNSET", ":-", "\"kept\"");
        LiteralToken first = Literal(variable);
        LiteralToken second = aliasRemains ? Literal(variable) : Literal();
        LiteralToken owner = Literal(second, first);
        using (options.UseModifierValueResolver(owner, '\\', variables, _ => "owned"))
        {
            Assert.Equal("owned", variable.ResolveVariables('\\', variables, options));
            first.ReplaceResolvedTokens(Array.Empty<Token>());
            Assert.Equal(aliasRemains ? "owned" : "\"kept\"",
                variable.ResolveVariables('\\', variables, options));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedUnownedModifierLookupsDoNotRebuildOwnershipPaths(bool cyclic)
    {
        long Measure(int count)
        {
            ResolutionOptions options = new();
            Dictionary<string, string?> variables = new();
            VariableRefToken member = new("UNSET", ":-", "member");
            LiteralToken owner = Literal(Enumerable.Range(0, count)
                .Select(_ => (Token)new VariableRefToken("UNSET", ":-", "member")).ToArray());
            owner.TokenList.Add(member);
            if (cyclic) owner.TokenList.Add(owner);
            VariableRefToken outsider = new("UNSET", ":-", "outside");
            using (options.UseModifierValueResolver(owner, '\\', variables, _ => "owned"))
            {
                Assert.Equal("owned", member.ResolveVariables('\\', variables, options));
                Assert.Equal("outside", outsider.ResolveVariables('\\', variables, options));
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < count; i++)
                    Assert.Equal("outside", outsider.ResolveVariables('\\', variables, options));
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                owner.RemoveResolvedChild(0);
                Assert.Equal("outside", outsider.ResolveVariables('\\', variables, options));
                Assert.Equal("owned", member.ResolveVariables('\\', variables, options));
                return allocated;
            }
        }

        Measure(16);
        long small = Measure(128);
        long large = Measure(512);

        Assert.InRange(large, 0, 4 * small + 500_000);
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
    public void UnownedModifierLookupsPreservePublicSyntax(bool stageScope, bool updateInline, bool distinct)
    {
        const int count = 512;
        UnownedForwardingAggregate owner = new(count, distinct);
        Dockerfile document = Document(stageScope, owner);
        string original = document.ToString();

        Assert.Equal((stageScope ? "ENV RESULT=" : "FROM prefix") +
            String.Concat(Enumerable.Repeat("\"outside\"", count)),
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(count, owner.LookupCount);
        if (!updateInline) Assert.Equal(original, document.ToString());
    }

    private sealed class UnownedForwardingAggregate : AggregateToken
    {
        private readonly VariableRefToken[] outsiders;

        public UnownedForwardingAggregate(int count, bool distinct) : base(
            Enumerable.Range(0, count).Select(_ => new VariableRefToken("UNUSED", ":-", "member")))
        {
            VariableRefToken shared = new("UNSET", ":-", "\"outside\"");
            outsiders = Enumerable.Range(0, count)
                .Select(_ => distinct ? new VariableRefToken("UNSET", ":-", "\"outside\"") : shared).ToArray();
        }

        public int LookupCount { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            bool inline = options!.UpdateInline;
            options.UpdateInline = false;
            try
            {
                return String.Concat(outsiders.Select(variable =>
                {
                    LookupCount++;
                    return variable.ResolveVariables(escapeChar, variables, options);
                }));
            }
            finally
            {
                options.UpdateInline = inline;
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeWrappersRetainSuppressedConsumerSerialization(bool stageScope, bool updateInline)
    {
        Dictionary<string, string?> state = new() { ["TEXT"] = "old" };
        VariableRefToken reference = new("IGNORED", ":-", "placeholder")
        {
            ModifierValueToken = Literal(new MutableSerializedString(() => state["TEXT"]!))
        };
        LiteralToken quoted = Literal(reference);
        quoted.QuoteChar = '\'';
        Dockerfile document = Document(stageScope, Literal(Literal(quoted)),
            new CallbackVariable(() => state["TEXT"] = "new"));

        Assert.Equal(stageScope ? "ENV RESULT=${IGNORED:-new}left" : "FROM prefix${IGNORED:-new}left",
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("IGNORED", reference.VariableName);
    }

    private sealed class DirectForwardingAggregate : AggregateToken
    {
        public DirectForwardingAggregate(IEnumerable<Token> tokens) : base(tokens) { }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null) =>
            String.Concat(Tokens.OfType<AggregateToken>().Select(token =>
                token.ResolveVariables(escapeChar, variables, options)));
    }

    [Theory]
    [InlineData("$SHARED")]
    [InlineData("${SHARED:-$UNUSED}")]
    public void ReferencedSyntaxBindingsAllocateLinearly(string syntax)
    {
        long Measure(int count)
        {
            Dockerfile document = Dockerfile.Parse("FROM alpine\nARG SHARED=value\n" +
                String.Concat(Enumerable.Range(0, count).Select(i => $"ARG X{i:D4}=placeholder\n")) +
                "ENV RESULT=ok");
            foreach (ArgInstruction instruction in document.Items.OfType<ArgInstruction>().Skip(1))
            {
                instruction.ArgTokens[0].ValueToken = new SerializedLiteral(syntax, "value");
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal("ENV RESULT=ok", document.ResolveVariables());
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Measure(16);
        long small = Measure(512);
        long large = Measure(1024);

        Assert.InRange(large, 0, 2 * small + 2_000_000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectiveSyntaxBindingsPreserveAbsentEmptyAndNestedNames(bool targetArg)
    {
        Dockerfile document = Dockerfile.Parse(
            "FROM alpine\nARG EMPTY=\"\"\nARG UPPER=upper\nARG lower=lower\nARG INNER=inner\n" +
            "ARG X=placeholder\nENV RESULT=$X");
        ArgInstruction instruction = document.Items.OfType<ArgInstruction>().Last();
        instruction.ArgTokens[0].ValueToken = new SerializedLiteral(
            "${EMPTY+set}/${UNSET-fallback}/${UPPER}/${lower}/${NESTED:-${INNER}}", "custom", variables =>
            {
                variables!["EMPTY"] = null;
                variables["UNSET"] = "new";
                variables["UPPER"] = "new";
                variables["lower"] = "new";
                variables["NESTED"] = "new";
                variables["INNER"] = "new";
            });
        string original = document.ToString();

        Assert.Equal(targetArg ? "ARG X=set/fallback/upper/lower/inner\n" : "ENV RESULT=custom",
            targetArg ? document.ResolveVariables(instruction) : document.ResolveVariables());
        Assert.Equal(original, document.ToString());
    }


    private sealed class DelegatingVariable : VariableRefToken
    {
        private readonly Action beforeResolution;

        public DelegatingVariable(Action beforeResolution) : base("UNSET", ":-", "\"kept\"") =>
            this.beforeResolution = beforeResolution;

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            beforeResolution();
            return base.ResolveVariables(escapeChar, variables, options);
        }
    }

    [Fact]
    public void SpeculativeQuoteFailureIsRethrownUnchanged()
    {
        InvalidOperationException expected = new("Speculative quote.");
        Dockerfile dockerfile = Document(true, new QuotedString(() => throw expected),
            new CallbackVariable(() => { }));

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
