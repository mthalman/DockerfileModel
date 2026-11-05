using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class ResolutionGuardTests
{
    public static IEnumerable<object[]> SourceEdits()
    {
        foreach (bool inline in new[] { false, true })
        foreach (bool dictionaryCallback in new[] { false, true })
        foreach (string entry in new[] { "aggregate", "literal", "variable", "json", "volume" })
        foreach (string edit in new[] { "add", "remove", "replace", "reorder", "value", "primitive", "quote" })
        {
            yield return new object[] { inline, dictionaryCallback, entry, edit };
        }
    }

    [Theory]
    [MemberData(nameof(SourceEdits))]
    public void CallbacksCannotEditActiveSourceTokens(
        bool inline, bool dictionaryCallback, string entry, string edit)
    {
        MutableLiteral target = new(new StringToken("a"), new StringToken("b"));
        void Change()
        {
            switch (edit)
            {
                case "add":
                    target.Children.Add(new StringToken("c"));
                    break;
                case "remove":
                    target.Children.RemoveAt(0);
                    break;
                case "replace":
                    target.Children[0] = new StringToken("a");
                    break;
                case "reorder":
                    (target.Children[0], target.Children[1]) = (target.Children[1], target.Children[0]);
                    break;
                case "value":
                    target.Value = "ab";
                    break;
                case "primitive":
                    Assert.IsType<StringToken>(target.Children[0]).Value = "changed";
                    break;
                case "quote":
                    target.QuoteChar = '\'';
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(edit));
            }
        }
        Token callback = dictionaryCallback ? new VariableRefToken("CALLBACK") : new CallbackLiteral(Change);
        target.Children.Add(callback);
        AggregateToken source = entry switch
        {
            "aggregate" => new Container(target),
            "literal" => target,
            "variable" => new VariableRefToken("UNSET", ":-", "") { ModifierValueToken = target },
            "json" => JsonSource(),
            "volume" => VolumeSource(),
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };
        AggregateToken JsonSource()
        {
            target.QuoteChar = '"';
            target.ResolveAsJsonString = true;
            return target;
        }
        AggregateToken VolumeSource()
        {
            VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"placeholder\"]");
            instruction.PathTokens[0].ReplaceWithToken(target);
            return instruction;
        }
        IDictionary<string, string?> variables = dictionaryCallback
            ? new CallbackDictionary(Change) : new Dictionary<string, string?>();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            source.ResolveVariables('\\', variables, new ResolutionOptions { UpdateInline = inline }));

        Assert.Equal(ResolutionGuard.StructuralMutationMessage, error.Message);
        Assert.Equal(edit switch { "add" => 4, "remove" => 2, _ => 3 }, target.Children.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedWordSettersRejectNoOpAssignmentsDuringCallbacks(bool inline)
    {
        CopyInstruction instruction = CopyInstruction.Parse("COPY pre\"$ROOT\" /dest");
        LiteralToken source = Assert.Single(instruction.SourceTokens);
        Token[] original = source.Tokens.ToArray();
        foreach (bool changeQuote in new[] { false, true })
        {
            CallbackDictionary variables = new(() =>
            {
                if (changeQuote)
                {
                    source.QuoteChar = source.QuoteChar;
                }
                else
                {
                    source.Value = source.Value;
                }
            });

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                instruction.ResolveVariables('\\', variables, new ResolutionOptions { UpdateInline = inline }));

            Assert.Equal(ResolutionGuard.StructuralMutationMessage, error.Message);
            Assert.Equal(original, source.Tokens);
            Assert.Equal("COPY pre\"$ROOT\" /dest", instruction.ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuardedValueSettersRejectBeforeReplacingChildren(bool inline)
    {
        LiteralToken source = new("$ROOT", canContainVariables: true);
        Token original = Assert.Single(source.Tokens);
        CallbackDictionary variables = new(() => source.Value = "changed");

        Assert.Throws<InvalidOperationException>(() => source.ResolveVariables('\\', variables,
            new ResolutionOptions { UpdateInline = inline }));

        Assert.Same(original, Assert.Single(source.Tokens));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallbackEnvironmentAndOptionChangesRemainSupported(bool inline)
    {
        StringToken prefix = new("before/");
        ResolutionOptions options = new() { UpdateInline = inline };
        Dictionary<string, string?> variables = new() { ["ROOT"] = "\\before" };
        CallbackLiteral callback = new(() =>
        {
            variables["ROOT"] = "\\after";
            options.RemoveEscapeCharacters = true;
        });
        Container source = new(prefix, new VariableRefToken("ROOT"), callback);

        Assert.Equal("before/after", source.ResolveVariables('\\', variables, options));
    }

    [Fact]
    public void LibraryPublicationsRemainAllowedDuringReentrantCallbacks()
    {
        Container nested = new(new VariableRefToken("ROOT"));
        CallbackLiteral callback = new(() =>
        {
            Assert.Equal("value", nested.ResolveVariables('\\',
                new Dictionary<string, string?> { ["ROOT"] = "value" },
                new ResolutionOptions { UpdateInline = true }));
        });
        Container source = new(nested, callback);

        Assert.Equal("value", source.ResolveVariables('\\'));
        Assert.IsType<StringToken>(Assert.Single(nested.Tokens));
    }

    [Fact]
    public void UnrelatedTreesCanBeEditedDuringACallback()
    {
        LiteralToken unrelated = new("before");
        Container source = new(new CallbackLiteral(() => unrelated.Value = "after"));

        Assert.Equal("", source.ResolveVariables('\\'));
        Assert.Equal("after", unrelated.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtectedQuoteAdoptionRejectsBeforeChangingTheReceivingTree(bool inline)
    {
        VolumeInstruction source = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        VolumeInstruction target = VolumeInstruction.Parse("VOLUME []");
        CallbackDictionary variables = new(() =>
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                target.PathTokens.Add(source.PathTokens[0]));
            Assert.Equal(ResolutionGuard.StructuralMutationMessage, error.Message);
            Assert.Equal("VOLUME []", target.ToString());
        }) { ["ROOT"] = "root" };

        Assert.Equal("VOLUME [\"root\"]", source.ResolveVariables('\\', variables,
            new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(inline ? "VOLUME [\"root\"]" : "VOLUME [\"$ROOT\"]", source.ToString());
    }

    [Fact]
    public void UnrelatedPrimitiveMutationAllocationsDoNotScaleWithProtectedSourceSize()
    {
        long Measure(int count)
        {
            Container source = new(Enumerable.Range(0, count)
                .Select(index => (Token)new Container(new StringToken(index.ToString()))).ToArray());
            StringToken unrelated = new("before");
            using ResolutionGuard.Scope scope = ResolutionGuard.Enter(source);
            using ResolutionGuard.CallbackScope callback = ResolutionGuard.Callback();
            unrelated.Value = "after";
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 512; index++)
            {
                unrelated.Value = "after";
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        long small = Measure(512);
        long large = Measure(2048);
        Assert.True(large <= small + 4096, $"Primitive setter allocations grew from {small} to {large} bytes.");
    }

    [Fact]
    public void CompletedReentrantSourcesCanBeEditedAndResolvedAgain()
    {
        LiteralToken nested = new("$ROOT", canContainVariables: true);
        Container source = new(new CallbackLiteral(() =>
        {
            Assert.Equal("", nested.ResolveVariables('\\', new CallbackDictionary(() => { })));
            nested.Value = "after";
            Assert.Equal("after", nested.ResolveVariables('\\', new CallbackDictionary(() => { })));
        }));

        Assert.Equal("", source.ResolveVariables('\\'));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    public void RepeatedRootScopesRetainProtectionUntilTheirLastRelease(int aliases)
    {
        ArgInstruction shared = new("X", "before");
        Dockerfile document = new(Enumerable.Repeat(shared, aliases));
        using (ResolutionGuard.Enter(document))
        {
            using (ResolutionGuard.Callback())
            {
                for (int i = 0; i < aliases; i++)
                {
                    using (ResolutionGuard.Enter(shared))
                    using (ResolutionGuard.Callback())
                    {
                        Assert.Throws<InvalidOperationException>(() => shared.ArgTokens[0].Value = "inside");
                    }
                    Assert.Throws<InvalidOperationException>(() => shared.ArgTokens[0].Value = "outside");
                }
            }
        }

        shared.ArgTokens[0].Value = "released";
        Assert.Equal("released", shared.ArgTokens[0].Value);
    }

    [Fact]
    public void RootScopeReferencesUseIdentityRatherThanConsumerEquality()
    {
        EqualArg first = new("FIRST");
        EqualArg second = new("SECOND");
        using (ResolutionGuard.Enter(first))
        using (ResolutionGuard.Callback())
        {
            using (ResolutionGuard.Enter(second))
            using (ResolutionGuard.Callback())
            {
                Assert.Throws<InvalidOperationException>(() => second.ArgTokens[0].Value = "inside");
            }
            second.ArgTokens[0].Value = "released";
            Assert.Throws<InvalidOperationException>(() => first.ArgTokens[0].Value = "inside");
        }
        first.ArgTokens[0].Value = "released";
    }

    [Fact]
    public void QuotableLeafBaselinesDeduplicateAliasesByReferenceIdentity()
    {
        CountingQuoteString first = new();
        CountingQuoteString second = new();
        LiteralToken owner = new(new Token[] { first, second, first }, true, '\\');
        ResolutionGuard.Snapshot snapshot = new(owner, owner);
        foreach (Token child in owner.Tokens)
        {
            snapshot.CaptureChildQuote(child);
        }

        snapshot.CaptureQuote();
        Assert.Equal(1, first.QuoteReads);
        Assert.Equal(1, second.QuoteReads);
        snapshot.Verify();
        Assert.Equal(2, first.QuoteReads);
        Assert.Equal(2, second.QuoteReads);
        second.QuoteChar = '"';
        Assert.Throws<InvalidOperationException>(snapshot.Verify);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolvedStringSnapshotsPreserveCurrentValueAndProvenanceDuringCallbacks(bool edited)
    {
        ResolvedStringToken original = new("\"a b\"", "a b", null, false, '\\');
        if (edited) original.Value = "edited";
        Container source = new(original, new VariableRefToken("ROOT"));
        ResolvedStringToken snapshot;
        using (ResolutionGuard.Enter(source))
        using (ResolutionGuard.Callback())
        {
            snapshot = original.CreateSnapshot();
            Assert.Throws<InvalidOperationException>(() => original.Value = "inside");
            Assert.Equal(edited ? "edited" : "\"a b\"", original.Value);
        }
        original.Value = "outside";

        Assert.Equal(edited ? "edited" : "\"a b\"", snapshot.Value);
        Assert.Equal("a b", snapshot.SemanticValue);
        Assert.Equal(!edited, snapshot.MatchesContext(null, false, '\\'));
        Assert.False(snapshot.MatchesContext('"', false, '\\'));
        Assert.False(snapshot.MatchesContext(null, true, '\\'));
        Assert.False(snapshot.MatchesContext(null, false, '`'));
    }

    [Fact]
    public void ResolvedStringSnapshotsAllocateLinearlyDuringCallbacks()
    {
        long Measure(int count)
        {
            Container source = new(Enumerable.Range(0, count)
                .Select(_ => (Token)new VariableRefToken("ROOT")).ToArray());
            ResolvedStringToken original = new("syntax", "semantic", null, false, '\\');
            using (ResolutionGuard.Enter(source))
            using (ResolutionGuard.Callback())
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < count; i++)
                {
                    Assert.Equal("semantic", original.CreateSnapshot().SemanticValue);
                }
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
        }

        Measure(16);
        long small = Measure(128);
        long large = Measure(512);

        Assert.InRange(large, 0, 4 * small + 250_000);
    }

    [Fact]
    public void TokensExposeACachedLiveEditableViewRatherThanTheRawList()
    {
        LiteralToken source = new("before");
        IList<Token> children = Assert.IsAssignableFrom<IList<Token>>(source.Tokens);
        Assert.IsNotType<List<Token>>(children);
        Assert.Same(children, source.Tokens);
        children.Add(new StringToken("/tail"));
        Assert.Equal("before/tail", source.ToString());
        children[0] = new StringToken("after");
        children.RemoveAt(1);
        Assert.Equal("after", source.ToString());
        children.Clear();
        Assert.Empty(source.Tokens);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("replace")]
    [InlineData("clear")]
    public void CallbacksCannotMutateNativeTokensThroughThePublicView(string edit)
    {
        LiteralToken source = new("$ROOT", canContainVariables: true);
        IList<Token> children = Assert.IsAssignableFrom<IList<Token>>(source.Tokens);
        Token original = Assert.Single(children);
        CallbackDictionary variables = new(() =>
        {
            switch (edit)
            {
                case "add":
                    children.Add(new StringToken("tail"));
                    break;
                case "remove":
                    children.RemoveAt(0);
                    break;
                case "replace":
                    children[0] = new StringToken("other");
                    break;
                case "clear":
                    children.Clear();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(edit));
            }
        });

        Assert.Throws<InvalidOperationException>(() => source.ResolveVariables('\\', variables));
        Assert.Same(original, Assert.Single(children));
    }

    [Fact]
    public void RawListMutationCannotBeHiddenByRestoringTheOriginalChildren()
    {
        MutableLiteral source = new(new VariableRefToken("ROOT"));
        Token original = Assert.Single(source.Tokens);
        CallbackDictionary variables = new(() =>
        {
            source.Children[0] = new StringToken("replacement");
            source.Children[0] = original;
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            source.ResolveVariables('\\', variables));
        Assert.Equal(ResolutionGuard.StructuralMutationMessage, error.Message);
        Assert.Same(original, Assert.Single(source.Tokens));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeQuoteSettersRejectSourceEdits(bool identifier)
    {
        EnvInstruction source = EnvInstruction.Parse("ENV NAME=$ROOT");
        IQuotableToken token = identifier
            ? source.VariableTokens[0].KeyToken
            : source.VariableTokens[0].ValueToken!;
        char? original = token.QuoteChar;
        CallbackDictionary variables = new(() => token.QuoteChar = '"');

        Assert.Throws<InvalidOperationException>(() => source.ResolveVariables('\\', variables));
        Assert.Equal(original, token.QuoteChar);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscapedEnvQuoteSettersRejectBeforeChangingSyntaxState(bool inline)
    {
        EnvInstruction source = EnvInstruction.Parse("ENV A=$ROOT X=\"a\\\"b\"");
        EnvEscapedQuoteLiteralToken value = Assert.IsType<EnvEscapedQuoteLiteralToken>(
            source.VariableTokens[1].ValueToken);
        string original = source.ToString();
        char? originalQuote = value.QuoteChar;
        Assert.True(value.HasOriginalEscapedQuoteSyntax);

        Assert.Throws<InvalidOperationException>(() => source.ResolveVariables('\\',
            new CallbackDictionary(() => value.QuoteChar = originalQuote == '"' ? '\'' : '"'),
            new ResolutionOptions { UpdateInline = inline }));

        Assert.Equal(originalQuote, value.QuoteChar);
        Assert.Equal(original, source.ToString());
        Assert.True(value.HasOriginalEscapedQuoteSyntax);
    }

    [Fact]
    public void EscapedEnvQuoteSettersRejectBeforeChangingSyntaxState()
    {
        EnvInstruction source = EnvInstruction.Parse("ENV A=$ROOT X=\"a\\\"b\"");
        EnvEscapedQuoteLiteralToken value =
            Assert.IsType<EnvEscapedQuoteLiteralToken>(source.VariableTokens[1].ValueToken);
        CallbackDictionary variables = new(() => value.QuoteChar = '\'');
        string original = source.ToString();

        Assert.Throws<InvalidOperationException>(() => source.ResolveVariables('\\', variables));
        Assert.Equal(original, source.ToString());
        Assert.True(value.HasOriginalEscapedQuoteSyntax);
    }

    [Fact]
    public void CustomQuoteOverridesAreCheckedAtCallbackBoundariesWithoutRollback()
    {
        CustomQuoteLiteral source = new();
        CallbackDictionary variables = new(() => source.QuoteChar = '"');

        Assert.Throws<InvalidOperationException>(() => source.ResolveVariables('\\', variables));
        Assert.Equal('"', source.QuoteChar);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CustomPrimitiveQuotesAreCheckedAtCallbackBoundariesWithoutRollback(bool inline, bool dictionaryCallback)
    {
        CustomQuoteString primitive = new();
        void Change() => primitive.QuoteChar = '"';
        Token callback = dictionaryCallback ? new VariableRefToken("ROOT") : new CallbackLiteral(Change);
        LiteralToken source = new(new Token[] { primitive, callback }, true, '\\');
        IDictionary<string, string?> variables = dictionaryCallback
            ? new CallbackDictionary(Change) : new Dictionary<string, string?>();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => source.ResolveVariables('\\', variables,
            new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(ResolutionGuard.StructuralMutationMessage, error.Message);
        Assert.Equal('"', primitive.QuoteChar);
        primitive.Value = "outside";
    }

    [Fact]
    public void LargeQuoteCaptureUsesReferenceIdentityAndChecksEachDistinctLeafOnce()
    {
        const int count = 8192;
        CountingQuoteString[] leaves = Enumerable.Range(0, count).Select(_ => new CountingQuoteString()).ToArray();
        LiteralToken source = new(leaves.SelectMany(leaf => new Token[] { leaf, leaf }), true, '\\');

        using (ResolutionGuard.Scope scope = ResolutionGuard.Enter(source))
        {
            using ResolutionGuard.CallbackScope callback = ResolutionGuard.Callback();
        }
        Assert.All(leaves, leaf => Assert.Equal(3, leaf.Reads));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedModifierPrimitiveQuotesRemainProtected(bool inline)
    {
        CustomQuoteString primitive = new();
        LiteralToken operand = new(new Token[] { primitive }, true, '\\');
        VariableRefToken reference = new("UNSET", ":-", "") { ModifierValueToken = operand };
        Container source = new(new CallbackLiteral(() => primitive.QuoteChar = '"'), reference);

        Assert.Throws<InvalidOperationException>(() => source.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal('"', primitive.QuoteChar);
        primitive.Value = "outside";
    }

    [Fact]
    public void CallbackFreePublicationDoesNotReadInactivePrimitiveQuoteGetters()
    {
        LiteralToken operand = new(new Token[] { new UnreadableQuoteString() }, true, '\\');
        VariableRefToken reference = new("SET", ":-", "") { ModifierValueToken = operand };

        Assert.Equal("value", reference.ResolveVariables('\\',
            new Dictionary<string, string?> { ["SET"] = "value" },
            new ResolutionOptions { UpdateInline = true }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LibraryPublishedCustomPrimitiveQuotesAreProtected(bool bulk)
    {
        LiteralToken source = new("before");
        CustomQuoteString published = new();
        void PublishAndChange()
        {
            using ResolutionGuard.Scope scope = ResolutionGuard.Enter(source);
            using ResolutionGuard.CallbackScope callback = ResolutionGuard.Callback();
            if (bulk)
            {
                source.ReplaceResolvedTokens(new[] { published });
            }
            else
            {
                source.ReplaceResolvedChild(0, published);
            }
            published.QuoteChar = '"';
        }

        Assert.Throws<InvalidOperationException>(PublishAndChange);
        Assert.Same(published, Assert.Single(source.Tokens));
    }

    [Fact]
    public void CompletedReentrantSourcesReleaseCustomPrimitiveQuoteChecks()
    {
        CustomQuoteString primitive = new();
        LiteralToken nested = new(new Token[] { primitive, new VariableRefToken("ROOT") }, true, '\\');
        Container source = new(new CallbackLiteral(() =>
        {
            Assert.Equal("before", nested.ResolveVariables('\\', new CallbackDictionary(() => { })));
            primitive.QuoteChar = '"';
        }));

        Assert.Equal("", source.ResolveVariables('\\'));
        Assert.Equal('"', primitive.QuoteChar);
    }

    [Fact]
    public void FailedReentrantQuoteCaptureReleasesItsIndependentSource()
    {
        LiteralToken nested = new(new Token[] { new UnreadableQuoteString(), new VariableRefToken("ROOT") }, true, '\\');
        Container source = new(new CallbackLiteral(() =>
        {
            Assert.Throws<InvalidOperationException>(() =>
                nested.ResolveVariables('\\', new CallbackDictionary(() => { })));
            nested.Value = "after";
        }));

        Assert.Equal("", source.ResolveVariables('\\'));
        Assert.Equal("after", nested.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameValueSourceAssignmentsAreRejected(bool quote)
    {
        LiteralToken source = new("$ROOT", canContainVariables: true);
        StringToken name = Assert.IsType<VariableRefToken>(Assert.Single(source.Tokens)).VariableNameToken;
        char? originalQuote = source.QuoteChar;
        string originalName = name.Value;
        CallbackDictionary variables = new(() =>
        {
            if (quote)
            {
                source.QuoteChar = originalQuote;
            }
            else
            {
                name.Value = originalName;
            }
        });

        Assert.Throws<InvalidOperationException>(() => source.ResolveVariables('\\', variables));
        Assert.Equal("$ROOT", source.ToString());
    }

    [Fact]
    public void RetiredPrimitiveSourcesRemainProtectedAfterLibraryPublication()
    {
        VariableRefToken reference = new("ROOT");
        StringToken name = reference.VariableNameToken;
        Container source = new(new CallbackLiteral(() => name.Value = "changed"), reference);

        Assert.Throws<InvalidOperationException>(() => source.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "value" },
            new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("ROOT", name.Value);
    }

    [Theory]
    [InlineData(false, "value")]
    [InlineData(false, "primitive")]
    [InlineData(false, "quote")]
    [InlineData(true, "value")]
    [InlineData(true, "primitive")]
    [InlineData(true, "quote")]
    public void FirstCallbacksCannotEditCompletedModifierSources(bool inline, string edit)
    {
        LiteralToken operand = new("before");
        StringToken primitive = Assert.IsType<StringToken>(Assert.Single(operand.Tokens));
        VariableRefToken reference = new("UNSET", ":-", "") { ModifierValueToken = operand };
        CallbackLiteral callback = new(() =>
        {
            switch (edit)
            {
                case "value":
                    operand.Value = "after";
                    break;
                case "primitive":
                    primitive.Value = "after";
                    break;
                case "quote":
                    operand.QuoteChar = '"';
                    break;
            }
        });
        Container source = new(callback, reference);

        Assert.Throws<InvalidOperationException>(() => source.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal("before", operand.Value);
        Assert.Null(operand.QuoteChar);
        if (inline)
        {
            Assert.IsType<StringToken>(source.Tokens.Last());
        }
        operand.Value = "outside";
    }

    [Fact]
    public void CallbackFreePublicationDoesNotReadInactiveCustomQuoteGetters()
    {
        VariableRefToken reference = new("SET", ":-", "") { ModifierValueToken = new UnreadableQuoteLiteral() };
        Container source = new(reference);

        Assert.Equal("value", source.ResolveVariables('\\',
            new Dictionary<string, string?> { ["SET"] = "value" },
            new ResolutionOptions { UpdateInline = true }));
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("all")]
    public void LibraryPublicationsDoNotReleaseOriginalPrimitiveProtection(string operation)
    {
        StringToken original = new("before");
        Container source = new(original);
        using ResolutionGuard.Scope scope = ResolutionGuard.Enter(source);
        using (ResolutionGuard.CallbackScope callback = ResolutionGuard.Callback())
        {
            switch (operation)
            {
                case "replace":
                    source.ReplaceResolvedChild(0, new StringToken("after"));
                    break;
                case "remove":
                    source.RemoveResolvedChild(0);
                    break;
                case "all":
                    source.ReplaceResolvedTokens(new[] { new StringToken("after") });
                    break;
            }
            Assert.Throws<InvalidOperationException>(() => original.Value = "changed");
        }
        Assert.Equal("before", original.Value);
        original.Value = "outside";
    }

    [Fact]
    public void PrimitiveProtectionUsesReferenceIdentityRatherThanConsumerEquality()
    {
        NeverEqualString primitive = new("before");
        LiteralToken source = new(new Token[] { primitive, new VariableRefToken("ROOT") }, true, '\\');

        Assert.Throws<InvalidOperationException>(() =>
            source.ResolveVariables('\\', new CallbackDictionary(() => primitive.Value = "changed")));
        Assert.Equal("before", primitive.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrimitiveMembershipRetainsAliasesAcrossRootsAndReleasesCompletedSources(bool initializeEarly)
    {
        StringToken shared = new("shared");
        StringToken nestedOnly = new("nested");
        StringToken published = new("published");
        Container outer = new(shared, shared);
        Container inner = new(shared, nestedOnly);
        using (ResolutionGuard.Enter(outer))
        using (ResolutionGuard.Callback())
        {
            if (initializeEarly)
            {
                new StringToken("").Value = "unrelated";
            }
            using (ResolutionGuard.Enter(inner))
            using (ResolutionGuard.Callback())
            {
                inner.ReplaceResolvedTokens(new Token[] { published, published });
                Assert.Throws<InvalidOperationException>(() => shared.Value = "changed");
                Assert.Throws<InvalidOperationException>(() => nestedOnly.Value = "changed");
                Assert.Throws<InvalidOperationException>(() => published.Value = "changed");
                StringToken replacement = new("replacement");
                inner.ReplaceResolvedChild(0, replacement);
                Assert.Throws<InvalidOperationException>(() => replacement.Value = "changed");
                Assert.Throws<InvalidOperationException>(() => published.Value = "changed");
            }
            nestedOnly.Value = "released";
            published.Value = "released";
            Assert.Throws<InvalidOperationException>(() => shared.Value = "changed");
        }
        shared.Value = "released";
    }

    [Fact]
    public void DocumentProtectionUsesReferenceIdentityRatherThanConsumerEquality()
    {
        NeverEqualDocument document = new(Dockerfile.Parse("ARG ROOT\nFROM base\n").Items);

        Assert.Throws<InvalidOperationException>(() =>
            document.ResolveVariables(new CallbackDictionary(() => document.Items.RemoveAt(0))));
        Assert.Equal("ARG ROOT\nFROM base\n", document.ToString());
    }

    [Fact]
    public void FailedResolutionReleasesTheGuard()
    {
        LiteralToken source = new("$ROOT", canContainVariables: true);
        Assert.Throws<InvalidOperationException>(() =>
            source.ResolveVariables('\\', new CallbackDictionary(() => source.Value = "changed")));

        source.Value = "after";
        Assert.Equal("after", source.ResolveVariables('\\'));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DocumentResolutionProtectsConstructMembershipAndOtherInstructions(bool inline, bool allInstructions)
    {
        Dockerfile document = Dockerfile.Parse("ARG ROOT\nFROM base\nENV A=$ROOT\nENV B=before\n");
        EnvInstruction first = document.Items.OfType<EnvInstruction>().First();
        EnvInstruction second = document.Items.OfType<EnvInstruction>().Last();
        ResolutionOptions options = new() { UpdateInline = inline };
        CallbackDictionary variables = new(() => second.VariableTokens[0].Value = "after");
        string? Resolve(IDictionary<string, string?> values) => allInstructions
            ? document.ResolveVariables(values, options)
            : document.ResolveVariables(first, values, options);

        Assert.Throws<InvalidOperationException>(() => Resolve(variables));
        Assert.Equal("before", second.VariableTokens[0].Value);
        Assert.Throws<InvalidOperationException>(() => Resolve(new CallbackDictionary(() =>
            Assert.IsType<StringToken>(Assert.Single(second.VariableTokens[0].ValueToken!.Tokens)).Value = "after")));
        Assert.Equal("before", second.VariableTokens[0].Value);
        Assert.Throws<InvalidOperationException>(() => Resolve(new CallbackDictionary(() => document.Items.RemoveAt(0))));
        Assert.Equal(4, document.Items.Count(item => item is Instruction));
        Assert.Throws<InvalidOperationException>(() =>
            Resolve(new CallbackDictionary(() => document.AddRaw(new VolumeInstruction("/later")))));
        Assert.Equal(4, document.Items.Count(item => item is Instruction));
    }

    [Fact]
    public void NativeCommandSettersCannotChangeAnotherActiveInstruction()
    {
        Dockerfile document = Dockerfile.Parse("ARG ROOT\nFROM base\nHEALTHCHECK CMD echo ok\n");
        HealthCheckInstruction healthcheck = Assert.Single(document.Items.OfType<HealthCheckInstruction>());
        Command? command = healthcheck.Command;

        Assert.Throws<InvalidOperationException>(() =>
            document.ResolveVariables(new CallbackDictionary(() => healthcheck.Command = null)));
        Assert.Same(command, healthcheck.Command);
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(8000)]
    public void LargeNativeOptionsCallbackWorkloadsKeepSourceTextUnchanged(int count)
    {
        StringToken suffix = new("suffix");
        Token[] children = Enumerable.Range(0, count)
            .SelectMany(_ => new Token[] { new StringToken("."), new VariableRefToken("X") })
            .Append(suffix).ToArray();
        LiteralToken root = new(children, true, '\\');
        int version = 0;
        ResolutionOptions options = new();
        CallbackDictionary variables = new(() => options.RemoveEscapeCharacters = ++version % 2 == 0)
        {
            ["X"] = "x"
        };
        string original = root.ToString();

        Assert.Equal(string.Concat(Enumerable.Repeat(".x", count)) + "suffix",
            root.ResolveVariables('\\', variables, options));
        Assert.Equal(count, version);
        Assert.Equal(original, root.ToString());
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(8000)]
    public void LargeNativeScalarCallbackWorkloadsRejectSourceEdits(int count)
    {
        StringToken suffix = new("suffix");
        LiteralToken root = new(Enumerable.Range(0, count)
            .Select(_ => (Token)new VariableRefToken("ROOT")).Append(suffix), true, '\\');

        Assert.Throws<InvalidOperationException>(() =>
            root.ResolveVariables('\\', new CallbackDictionary(() => suffix.Value = "changed")));
        Assert.Equal("suffix", suffix.Value);
    }

    private sealed class Container(params Token[] children) : AggregateToken(children);

    private sealed class EqualArg(string name) : ArgInstruction(name, "before")
    {
        public override bool Equals(object? obj) => obj is EqualArg;
        public override int GetHashCode() => 0;
    }

    private sealed class CountingQuoteString() : StringToken("before"), IQuotableToken
    {
        private char? quote;
        public int QuoteReads { get; private set; }
        public char? QuoteChar { get { QuoteReads++; return quote; } set => quote = value; }
        public override bool Equals(object? obj) => obj is CountingQuoteString;
        public override int GetHashCode() => 0;
    }

    private sealed class MutableLiteral(params Token[] children) : LiteralToken(children, true, '\\')
    {
        internal List<Token> Children => TokenList;
    }

    private sealed class CustomQuoteLiteral() : LiteralToken("$ROOT", canContainVariables: true)
    {
        public override char? QuoteChar { get; set; }
    }

    private sealed class CustomQuoteString() : StringToken("before"), IQuotableToken
    {
        public char? QuoteChar { get; set; }
    }

    private sealed class CountingQuoteString() : StringToken(""), IQuotableToken
    {
        internal int Reads { get; private set; }

        public char? QuoteChar
        {
            get { Reads++; return null; }
            set { }
        }

        public override bool Equals(object? obj) => obj is CountingQuoteString;
        public override int GetHashCode() => 0;
    }

    private sealed class UnreadableQuoteString() : StringToken("unused"), IQuotableToken
    {
        public char? QuoteChar
        {
            get => throw new InvalidOperationException("The inactive primitive quote getter must not run.");
            set { }
        }
    }

    private sealed class UnreadableQuoteLiteral() : LiteralToken("unused")
    {
        public override char? QuoteChar
        {
            get => throw new InvalidOperationException("The inactive quote getter must not run.");
            set { }
        }
    }

    private sealed class NeverEqualString(string value) : StringToken(value)
    {
        public override bool Equals(object? obj) => false;
        public override int GetHashCode() => 0;
    }

    private sealed class NeverEqualDocument(IEnumerable<DockerfileConstruct> items) : Dockerfile(items)
    {
        public override bool Equals(object? obj) => false;
        public override int GetHashCode() => 0;
    }

    private sealed class CallbackLiteral(Action callback) : LiteralToken("")
    {
        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            callback();
            return "";
        }
    }

    private sealed class CallbackDictionary(Action callback)
        : Dictionary<string, string?>, IDictionary<string, string?>
    {
        public new bool TryGetValue(string key, out string? value)
        {
            callback();
            return base.TryGetValue(key, out value);
        }
    }
}
