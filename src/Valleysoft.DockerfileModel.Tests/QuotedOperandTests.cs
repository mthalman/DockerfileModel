using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class QuotedOperandTests
{
    [Theory]
    [InlineData("\"foo\"bar", "foobar")]
    [InlineData("foo\"bar\"", "foobar")]
    [InlineData("\"foo\"\"bar\"", "foobar")]
    [InlineData("'foo'\"bar\"", "foobar")]
    [InlineData("\"foo bar\"baz", "foo barbaz")]
    [InlineData("\"foo\"bar\" baz\"", "foobar baz")]
    [InlineData("foo\\ bar\"baz\"", "foo\\ barbaz")]
    [InlineData("\"\"bar", "bar")]
    [InlineData("foo\"\"bar", "foobar")]
    public void AdjacentFragmentsAreOneFileOperand(string operand, string value)
    {
        foreach (string name in new[] { "COPY", "ADD" })
        {
            string text = $"{name} {operand} /dest";
            AssertFileTransferModels(text, instruction =>
            {
                Assert.Equal(new[] { value }, instruction.Sources);
                Assert.Equal("/dest", instruction.Destination);
                LiteralToken source = Assert.Single(instruction.SourceTokens);
                Assert.Equal(operand, source.ToString());
                Assert.Null(source.QuoteChar);
            });
        }
    }

    [Theory]
    [InlineData("\"/dir name\"", "/dir name", '"')]
    [InlineData("'/dir name'", "/dir name", '\'')]
    [InlineData("\"/dir \"name", "/dir name", null)]
    [InlineData("/dir\" name\"", "/dir name", null)]
    public void HeredocDestinationIsOneQuotedWord(string operand, string value, char? quote)
    {
        foreach (string name in new[] { "COPY", "ADD" })
        {
            string text = $"{name} <<EOF {operand}\nbody\nEOF\n";
            AssertFileTransferModels(text, instruction =>
            {
                Assert.Empty(instruction.Sources);
                Assert.Equal(value, instruction.Destination);
                Assert.Equal(quote, instruction.DestinationToken!.QuoteChar);
                Assert.Single(instruction.HeredocMarkerTokens);
                Assert.Equal("body\nEOF\n", Assert.Single(instruction.HeredocBodyTokens).ToString());
                instruction.Destination = "/updated";
                Assert.Equal("/updated", instruction.Destination);
                Assert.Equal($"{name} <<EOF {quote}/updated{quote}\nbody\nEOF\n", instruction.ToString());
            });
        }
    }

    [Theory]
    [InlineData("\"$ROOT\"suffix")]
    [InlineData("prefix\"${ROOT}\"suffix")]
    [InlineData("\"${ROOT}\"\"suffix\"")]
    public void VariableFragmentsPreserveSyntaxAndResolve(string operand)
    {
        string text = $"COPY {operand} /dest";
        AssertFileTransferModels(text, instruction =>
        {
            LiteralToken source = Assert.Single(instruction.SourceTokens);
            Assert.Contains(source.Tokens.OfType<LiteralToken>(),
                fragment => fragment.Tokens.OfType<VariableRefToken>().Any());
            Dictionary<string, string?> variables = new() { ["ROOT"] = "value" };
            string resolved = source.ResolveVariables(Dockerfile.DefaultEscapeChar, variables)!;
            Assert.Equal(operand.Replace("${ROOT}", "value").Replace("$ROOT", "value"), resolved);
            Assert.Equal(text, instruction.ToString());
            source.ResolveVariables(Dockerfile.DefaultEscapeChar, variables, new ResolutionOptions { UpdateInline = true });
            Assert.Equal(resolved, source.ToString());
            Assert.DoesNotContain("$", source.Value);
            source.Value = "replacement";
            Assert.Null(source.QuoteChar);
            Assert.Equal("COPY replacement /dest", instruction.ToString());
        });
    }

    [Fact]
    public void FreeformLiteralsRetainEmbeddedApostrophes()
    {
        const string text = "MAINTAINER Jean O'Brien";
        MaintainerInstruction instruction = MaintainerInstruction.Parse(text);
        Assert.Equal("Jean O'Brien", instruction.Maintainer);
        Assert.Equal(text, instruction.ToString());
        Assert.Equal(text, Dockerfile.Parse(text).ToString());
        Assert.Equal(text, Dockerfile.TryParse(text).Dockerfile!.ToString());
    }

    [Fact]
    public void WhitespaceStillSeparatesQuotedOperands()
    {
        AssertFileTransferModels("COPY \"foo\" \"bar\" /dest", instruction =>
        {
            Assert.Equal(new[] { "foo", "bar" }, instruction.Sources);
            Assert.All(instruction.SourceTokens, source => Assert.Equal('"', source.QuoteChar));
        });
    }

    [Fact]
    public void MixedDestinationKeepsCustomEscapeAndVariables()
    {
        const string text = "# escape=`\nCOPY <<EOF \"/${DIR} \"suffix\nbody\nEOF\n";
        Dockerfile model = Dockerfile.Parse(text);
        Assert.Equal(text, model.ToString());
        CopyInstruction instruction = Assert.Single(model.Items.OfType<CopyInstruction>());
        Assert.Equal("/${DIR} suffix", instruction.Destination);
        instruction.DestinationToken!.ResolveVariables('`', new Dictionary<string, string?> { ["DIR"] = "mydir" },
            new ResolutionOptions { UpdateInline = true });
        Assert.Equal("/mydir suffix", instruction.Destination);
        Assert.Equal("# escape=`\nCOPY <<EOF \"/mydir \"suffix\nbody\nEOF\n", model.ToString());
    }

    [Theory]
    [InlineData("foo\"bar")]
    [InlineData("foo'bar")]
    [InlineData("\"foo\"bar\"baz")]
    [InlineData("'foo'bar'baz")]
    [InlineData("\\conf\\\\\"")]
    public void UnmatchedOperandFragmentsCannotReturnTruncatedInstructions(string operand)
    {
        foreach (string name in new[] { "COPY", "ADD" })
        {
            string text = $"{name} {operand} /dest";
            AssertFileTransferModels(text, instruction =>
            {
                Assert.Equal(operand, Assert.Single(instruction.SourceTokens).ToString());
                Assert.Equal("/dest", instruction.Destination);
            });
        }
    }

    [Theory]
    [InlineData("ENV name=foo\"bar")]
    [InlineData("ENV name=foo'bar")]
    [InlineData("LABEL name=foo\"bar")]
    [InlineData("USER foo\"bar")]
    [InlineData("VOLUME foo\"bar")]
    public void NonFileOperandsRetainUnmatchedEmbeddedQuotes(string text)
    {
        Instruction standalone = text.Split(' ')[0] switch
        {
            "ENV" => EnvInstruction.Parse(text),
            "LABEL" => LabelInstruction.Parse(text),
            "USER" => UserInstruction.Parse(text),
            "VOLUME" => VolumeInstruction.Parse(text),
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(text, standalone.ToString());
        Assert.Equal(text, Dockerfile.Parse(text).ToString());
        DockerfileParseResult result = Dockerfile.TryParse(text);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(text, result.Dockerfile!.ToString());
    }

    [Theory]
    [InlineData("foo\"bar")]
    [InlineData("foo'bar")]
    [InlineData("\"foo\"bar\"baz")]
    public void HeredocOperandsRetainUnmatchedFragmentsInAllParserModes(string operand)
    {
        foreach (string name in new[] { "COPY", "ADD" })
        {
            AssertFileTransferModels($"{name} <<EOF {operand}\nbody\nEOF\n", instruction =>
            {
                Assert.Equal(operand, instruction.DestinationToken!.ToString());
                Assert.Empty(instruction.Sources);
            });
        }
    }

    [Theory]
    [InlineData("foo\" bar\"", "foo bar", '"')]
    [InlineData("foo\" bar\"", "foo bar", '\'')]
    [InlineData("\"foo bar\"baz", "foo barbaz", '"')]
    public void RequotingMixedOperandsPreservesTheirValueAndBoundary(string operand, string value, char quote)
    {
        CopyInstruction instruction = CopyInstruction.Parse($"COPY {operand} /dest");
        LiteralToken source = Assert.Single(instruction.SourceTokens);
        source.QuoteChar = source.QuoteChar;
        Assert.Equal(operand, source.ToString());
        source.QuoteChar = quote;
        Assert.Equal(value, source.Value);
        Assert.Equal($"{quote}{value}{quote}", source.ToString());
        CopyInstruction reparsed = CopyInstruction.Parse(instruction.ToString());
        Assert.Equal(new[] { value }, reparsed.Sources);
        Assert.Equal("/dest", reparsed.Destination);
    }

    [Fact]
    public void RequotingMixedOperandsRetainsVariableReferences()
    {
        CopyInstruction instruction = CopyInstruction.Parse("COPY prefix\"${ROOT} suffix\" /dest");
        LiteralToken source = Assert.Single(instruction.SourceTokens);
        source.QuoteChar = '"';
        Assert.Equal("\"prefix${ROOT} suffix\"", source.ToString());
        Assert.Equal("\"prefixvalue suffix\"", source.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "value" }));
        Assert.Equal(new[] { "prefix${ROOT} suffix" }, CopyInstruction.Parse(instruction.ToString()).Sources);
    }

    [Fact]
    public void StagedRequotingMixedOperandsMatchesCommittedSyntax()
    {
        LiteralToken source = Assert.Single(CopyInstruction.Parse("COPY foo\" bar\" /dest").SourceTokens);
        CopyInstruction target = CopyInstruction.Parse("COPY [\"old\", \"/dest\"]");
        target.SourceTokens.Add(source);
        Assert.Same(source, target.SourceTokens[1]);
        Assert.Equal("\"foo bar\"", source.ToString());
        Assert.Equal(new[] { "old", "foo bar" }, target.Sources);
        Assert.Equal(new[] { "old", "foo bar" }, CopyInstruction.Parse(target.ToString()).Sources);
    }

    [Theory]
    [InlineData("\"$ROOT\"suffix", "\"${ROOT}suffix\"")]
    [InlineData("\"$ROOT\"1", "\"${ROOT}1\"")]
    [InlineData("\"$ROOT\"_suffix", "\"${ROOT}_suffix\"")]
    [InlineData("\"$ROOT\"\"\"suffix", "\"${ROOT}suffix\"")]
    [InlineData("\"$ROOT\"/suffix", "\"$ROOT/suffix\"")]
    [InlineData("\"${ROOT}\"suffix", "\"${ROOT}suffix\"")]
    public void RequotingPreservesVariableNameBoundaries(string operand, string expected)
    {
        CopyInstruction instruction = CopyInstruction.Parse($"COPY {operand} /dest");
        LiteralToken source = Assert.Single(instruction.SourceTokens);
        VariableRefToken variable = Assert.Single(source.Tokens.OfType<LiteralToken>()
            .SelectMany(fragment => fragment.Tokens).OfType<VariableRefToken>());
        source.QuoteChar = '"';
        Assert.Same(variable, Assert.Single(source.Tokens.OfType<VariableRefToken>()));
        Assert.Equal(expected, source.ToString());
        Dictionary<string, string?> variables = new() { ["ROOT"] = "value", ["ROOTsuffix"] = "wrong" };
        LiteralToken reparsed = Assert.Single(CopyInstruction.Parse(instruction.ToString()).SourceTokens);
        Assert.Equal("ROOT", Assert.Single(reparsed.Tokens.OfType<VariableRefToken>()).VariableName);
        Assert.Equal(source.ResolveVariables('\\', variables), reparsed.ResolveVariables('\\', variables));
    }

    [Fact]
    public void StagedRequotingPreservesVariableNameBoundaries()
    {
        LiteralToken source = Assert.Single(CopyInstruction.Parse("COPY \"$ROOT\"suffix /dest").SourceTokens);
        LiteralToken fragment = Assert.Single(source.Tokens.OfType<LiteralToken>());
        VariableRefToken variable = Assert.Single(fragment.Tokens.OfType<VariableRefToken>());
        CopyInstruction target = CopyInstruction.Parse("COPY [\"old\", \"/dest\"]");
        target.SourceTokens.Add(source);
        Assert.Same(source, target.SourceTokens[1]);
        Assert.Same(variable, Assert.Single(source.Tokens.OfType<VariableRefToken>()));
        Assert.Equal("\"${ROOT}suffix\"", source.ToString());
        Dictionary<string, string?> variables = new() { ["ROOT"] = "value", ["ROOTsuffix"] = "wrong" };
        Assert.Equal(source.ResolveVariables('\\', variables),
            CopyInstruction.Parse(target.ToString()).SourceTokens[1].ResolveVariables('\\', variables));
    }

    [Fact]
    public void RejectedStagedRequotingDoesNotChangeVariableSyntax()
    {
        const string operand = "\"$ROOT\"suffix\\q";
        LiteralToken source = Assert.Single(CopyInstruction.Parse($"COPY {operand} /dest").SourceTokens);
        VariableRefToken variable = Assert.Single(source.Tokens.OfType<LiteralToken>()
            .SelectMany(fragment => fragment.Tokens).OfType<VariableRefToken>());
        CopyInstruction target = CopyInstruction.Parse("COPY [\"old\", \"/dest\"]");
        string before = target.ToString();
        Assert.Throws<InvalidOperationException>(() => target.SourceTokens.Add(source));
        Assert.Equal(before, target.ToString());
        Assert.Equal(operand, source.ToString());
        Assert.Equal("$ROOT", variable.ToString());
    }

    [Theory]
    [InlineData("/dir\" name\"", "/updated name")]
    [InlineData("\"/dir \"name", "/updated name")]
    [InlineData("/dir' name'", "/updated name")]
    [InlineData("/dir\" name\"", "$ROOT/new dir")]
    [InlineData("/dir\" name\"", "/updated\tname")]
    [InlineData("/dir\" name\"", "/updated\" name")]
    public void MixedDestinationValueUpdatesPreserveOneOperand(string operand, string replacement)
    {
        foreach (string name in new[] { "COPY", "ADD" })
        {
            foreach (bool heredoc in new[] { false, true })
            {
                string text = heredoc ? $"{name} <<EOF {operand}\nbody\nEOF\n" : $"{name} src {operand}";
                AssertFileTransferModels(text, instruction =>
                {
                    LiteralToken destination = instruction.DestinationToken!;
                    instruction.Destination = instruction.Destination;
                    Assert.Equal(text, instruction.ToString());
                    instruction.Destination = replacement;
                    Assert.Same(destination, instruction.DestinationToken);
                    Assert.Equal(replacement, instruction.Destination);
                    FileTransferInstruction reparsed = name == "COPY"
                        ? CopyInstruction.Parse(instruction.ToString()) : AddInstruction.Parse(instruction.ToString());
                    Assert.Equal(replacement, reparsed.Destination);
                    Assert.Equal(heredoc ? Array.Empty<string>() : new[] { "src" }, reparsed.Sources);
                });
            }
        }
    }

    [Fact]
    public void MixedOperandUpdatesRetainCustomEscapeContext()
    {
        CopyInstruction instruction = CopyInstruction.Parse("COPY \"$ROOT\"suffix /dir\" name\"", '`');
        LiteralToken source = Assert.Single(instruction.SourceTokens);
        source.QuoteChar = '"';
        instruction.Destination = "/updated`\" dir";
        CopyInstruction reparsed = CopyInstruction.Parse(instruction.ToString(), '`');
        Assert.Equal(instruction.Destination, reparsed.Destination);
        Assert.Equal("ROOT", Assert.Single(reparsed.SourceTokens[0].Tokens.OfType<VariableRefToken>()).VariableName);
    }

    [Fact]
    public void RejectedMixedValueUpdateLeavesOriginalSyntax()
    {
        const string text = "COPY src /dir\" name\"";
        CopyInstruction instruction = CopyInstruction.Parse(text);
        Assert.Throws<ArgumentException>(() => instruction.Destination = "/updated'\" name");
        Assert.Equal(text, instruction.ToString());
        Assert.Equal("/dir name", instruction.Destination);
    }

    [Theory]
    [InlineData("foo\"bar\"")]
    [InlineData("foo'bar'")]
    [InlineData("/new dir")]
    public void UnquotedOperandUpdatesRetainTheirValueAfterReparsing(string replacement)
    {
        foreach (string name in new[] { "COPY", "ADD" })
        {
            AssertFileTransferModels($"{name} src /dest", instruction =>
            {
                instruction.Destination = replacement;
                Assert.Equal(replacement, instruction.Destination);
                FileTransferInstruction reparsed = name == "COPY"
                    ? CopyInstruction.Parse(instruction.ToString()) : AddInstruction.Parse(instruction.ToString());
                Assert.Equal(replacement, reparsed.Destination);
                Assert.Equal(new[] { "src" }, reparsed.Sources);
            });
        }
    }

    [Fact]
    public void MixedOperandUpdatesRetainTheirWordContextAcrossReplacements()
    {
        CopyInstruction instruction = CopyInstruction.Parse("COPY src /dir\" name\"");
        LiteralToken destination = instruction.DestinationToken!;
        instruction.Destination = "/first";
        Assert.Null(destination.QuoteChar);
        instruction.Destination = "/second dir";
        Assert.Same(destination, instruction.DestinationToken);
        Assert.Equal("/second dir", CopyInstruction.Parse(instruction.ToString()).Destination);
    }

    [Fact]
    public void ClearingAMixedSourceRetainsAnEmptyOperand()
    {
        CopyInstruction instruction = CopyInstruction.Parse("COPY \"foo\"bar /dest");
        LiteralToken source = Assert.Single(instruction.SourceTokens);
        source.Value = "";
        Assert.Equal("", source.Value);
        Assert.Equal(new[] { "" }, CopyInstruction.Parse(instruction.ToString()).Sources);
    }

    [Theory]
    [InlineData("foo\" bar\"")]
    [InlineData("\"$ROOT\"suffix")]
    public void ReplacingAStagedQuoteRestoresOriginalFragmentSyntax(string operand)
    {
        LiteralToken source = Assert.Single(CopyInstruction.Parse($"COPY {operand} /dest").SourceTokens);
        Token[] originalChildren = source.Tokens.ToArray();
        TokenEditPlan plan = new();
        plan.SetQuote(source, '"');
        Assert.NotEqual(operand, plan.Render(source));
        Assert.Equal(operand, source.ToString());
        plan.SetQuote(source, source.QuoteChar);
        Assert.Equal(operand, plan.Render(source));
        plan.Commit();
        Assert.Equal(operand, source.ToString());
        Assert.Equal(originalChildren, source.Tokens);
    }

    [Fact]
    public void StagedFragmentEditsAreIncludedInTheFinalQuoteNormalization()
    {
        LiteralToken source = Assert.Single(CopyInstruction.Parse("COPY foo\" bar\" /dest").SourceTokens);
        LiteralToken fragment = Assert.Single(source.Tokens.OfType<LiteralToken>());
        TokenEditPlan plan = new();
        plan.SetQuote(source, '"');
        plan.Edit(fragment)[0] = new StringToken(" changed");
        Assert.Equal("\"foo changed\"", plan.Render(source));
        plan.Commit();
        Assert.Equal("\"foo changed\"", source.ToString());
    }

    [Fact]
    public void RepeatedQuotePreviewsDoNotChangeStagedVariableChildren()
    {
        LiteralToken source = Assert.Single(CopyInstruction.Parse("COPY \"$ROOT\"suffix /dest").SourceTokens);
        LiteralToken fragment = Assert.Single(source.Tokens.OfType<LiteralToken>());
        VariableRefToken variable = Assert.Single(fragment.Tokens.OfType<VariableRefToken>());
        TokenEditPlan plan = new();
        plan.SetQuote(source, '"');
        Assert.Equal("\"${ROOT}suffix\"", plan.Render(source));
        plan.Edit(variable)[0] = new StringToken("UPDATED");
        Assert.Equal("\"${UPDATED}suffix\"", plan.Render(source));
        Assert.Single(plan.Tokens(variable));
        Assert.Equal("ROOT", variable.VariableName);
        plan.SetQuote(source, null);
        Assert.Equal("\"$UPDATED\"suffix", plan.Render(source));
        plan.SetQuote(source, '\'');
        Assert.Equal("'${UPDATED}suffix'", plan.Render(source));
        plan.Commit();
        Assert.Equal("'${UPDATED}suffix'", source.ToString());
        Assert.Equal("UPDATED", variable.VariableName);
    }

    [Theory]
    [InlineData("COPY")]
    [InlineData("ADD")]
    public void HeredocBodyCannotCloseADestinationQuote(string name)
    {
        string text = $"{name} <<EOF \"/dest\nbody\"\nEOF\n";
        Assert.Throws<ParseException>(() => name == "COPY"
            ? (FileTransferInstruction)CopyInstruction.Parse(text)
            : AddInstruction.Parse(text));
        Assert.False(Dockerfile.TryParse(text).Success);
    }

    [Fact]
    public void HeredocOperandTokenizationPreservesManySources()
    {
        string[] sources = Enumerable.Range(0, 300).Select(index => $"\"source {index}\"suffix").ToArray();
        string text = $"COPY <<EOF {string.Join("\t", sources)}\t\"/dir name\"\nbody\nEOF\n";
        AssertFileTransferModels(text, instruction =>
        {
            Assert.Equal(Enumerable.Range(0, 300).Select(index => $"source {index}suffix"), instruction.Sources);
            Assert.Equal("/dir name", instruction.Destination);
        });
    }

    private static void AssertFileTransferModels(string text, Action<FileTransferInstruction> assert)
    {
        FileTransferInstruction standalone = text.StartsWith("COPY", StringComparison.Ordinal)
            ? CopyInstruction.Parse(text)
            : AddInstruction.Parse(text);
        Assert.Equal(text, standalone.ToString());
        assert(standalone);
        AssertModel(Dockerfile.Parse(text));
        foreach (DockerfileParseMode mode in Enum.GetValues<DockerfileParseMode>())
        {
            DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions { Mode = mode });
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Empty(result.Diagnostics);
            AssertModel(result.Dockerfile!);
        }

        void AssertModel(Dockerfile model)
        {
            Assert.Equal(text, model.ToString());
            SourceSpanTests.AssertPartition(text, model);
            assert(Assert.IsAssignableFrom<FileTransferInstruction>(Assert.Single(model.Items)));
        }
    }
}
