using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class CommandJsonRecognitionTests
{
    public static IEnumerable<object[]> ShellFallbackCases()
    {
        string[] arguments =
        {
            "[\"echo\"] trailing",
            "[\"echo\"]#inline",
            "[\"echo\"] [\"second\"]",
            "[\"echo\" \"hello\"]",
            "[\"echo\"\"hello\"]",
            "[\"echo\",]",
            "[\"bad\\q\"]",
            "[\"echo\", 1] trailing",
            "[\"echo\" \\\n \"hello\"]"
        };
        foreach (string prefix in new[] { "RUN", "CMD", "ENTRYPOINT", "HEALTHCHECK CMD", "ONBUILD RUN" })
        {
            foreach (string argument in arguments)
            {
                yield return new object[] { prefix + " " + argument + "\n" };
            }
        }
    }

    [Theory]
    [MemberData(nameof(ShellFallbackCases))]
    public void InvalidJsonUsesShellFormWithoutDiscardingSource(string text)
    {
        Instruction standalone = ParseInstruction(text);
        Assert.Equal(text, standalone.ToString());
        Assert.IsType<ShellFormCommand>(GetCommand(standalone));

        string documentText = text + "RUN echo after\n";
        AssertDocument(Dockerfile.Parse(documentText));
        foreach (DockerfileParseMode mode in Enum.GetValues<DockerfileParseMode>())
        {
            DockerfileParseResult result = Dockerfile.TryParse(documentText, new DockerfileParseOptions { Mode = mode });
            Assert.True(result.Success);
            Assert.Empty(result.Diagnostics);
            AssertDocument(result.Dockerfile!);
            SourceSpanTests.AssertPartition(documentText, result.Dockerfile!);
        }

        void AssertDocument(Dockerfile document)
        {
            Assert.Equal(documentText, document.ToString());
            Assert.Equal(2, document.Items.Count);
            Assert.IsType<ShellFormCommand>(GetCommand(Assert.IsAssignableFrom<Instruction>(document.Items[0])));
            Assert.IsType<RunInstruction>(document.Items[1]);
        }
    }

    [Theory]
    [InlineData("[\"echo\", \"hello\"]\n")]
    [InlineData("[\"echo\", \\\n# comment\n \"hello\"]\n")]
    [InlineData("[\"ec\\\nho\"]\n")]
    [InlineData("[ ] \n")]
    public void ValidJsonRemainsExecForm(string arguments)
    {
        string text = "RUN " + arguments;
        Assert.IsType<ExecFormCommand>(RunInstruction.Parse(text).Command);
        Assert.Equal(text, Dockerfile.Parse(text).ToString());
        DockerfileParseResult result = Dockerfile.TryParse(text + "RUN echo after\n");
        Assert.True(result.Success);
        Assert.IsType<ExecFormCommand>(Assert.IsType<RunInstruction>(result.Dockerfile!.Items[0]).Command);
        Assert.Equal(text + "RUN echo after\n", result.Dockerfile.ToString());
    }

    [Theory]
    [InlineData("RUN")]
    [InlineData("CMD")]
    [InlineData("ENTRYPOINT")]
    [InlineData("HEALTHCHECK CMD")]
    [InlineData("ONBUILD RUN")]
    public void WhitespaceOnlyJsonElementsRemainExecForm(string prefix)
    {
        string text = prefix + " [\"true\", \" \", \"\", \"  leading\", \"trailing  \"]\n";
        AssertCommand(ParseInstruction(text));
        Assert.Equal(text, Dockerfile.Parse(text).ToString());
        DockerfileParseResult result = Dockerfile.TryParse(text);
        Assert.True(result.Success);
        Assert.Equal(text, result.Dockerfile!.ToString());
        AssertCommand(Assert.IsAssignableFrom<Instruction>(Assert.Single(result.Dockerfile.Items)));

        void AssertCommand(Instruction instruction)
        {
            ExecFormCommand command = Assert.IsType<ExecFormCommand>(GetCommand(instruction));
            Assert.Equal(new[] { "true", " ", "", "  leading", "trailing  " }, command.Values);
            Assert.Equal(text, instruction.ToString());
        }
    }

    [Theory]
    [InlineData("RUN [\"echo\", 1]\n")]
    [InlineData("CMD [true]\n")]
    [InlineData("ENTRYPOINT [null]\n")]
    [InlineData("HEALTHCHECK CMD [[\"nested\"]]\n")]
    public void ValidNonStringArraysRemainErrors(string text)
    {
        Assert.Throws<ParseException>(() => ParseInstruction(text));
        Assert.Throws<ParseException>(() => Dockerfile.Parse(text));
        Assert.False(Dockerfile.TryParse(text).Success);
    }

    [Theory]
    [InlineData("[\"echo\" \"hello\"]")]
    [InlineData("[\"echo\"\"hello\"]")]
    public void ExplicitExecFormRequiresCommas(string text)
    {
        Assert.Throws<ParseException>(() => ExecFormCommand.Parse(text));
        Assert.Throws<ParseException>(() => ShellInstruction.Parse("SHELL " + text));
        Assert.False(Dockerfile.TryParse("SHELL " + text).Success);
    }

    [Theory]
    [InlineData("[\"echo\"] trailing")]
    [InlineData("[\"echo\",]")]
    [InlineData("[\"echo\"]\nRUN echo after")]
    public void ExplicitJsonParsersRejectIncompleteOrInvalidJson(string text)
    {
        Assert.Throws<ParseException>(() => ExecFormCommand.Parse(text));
        Assert.Throws<ParseException>(() => ShellInstruction.Parse("SHELL " + text));
    }

    [Fact]
    public void ExplicitExecKeepsLegacyLiteralSyntaxButShellRequiresJson()
    {
        string text = "[\"bad\\q\"]";
        Assert.Equal(text, ExecFormCommand.Parse(text).ToString());
        Assert.Throws<ParseException>(() => ShellInstruction.Parse("SHELL " + text));
    }

    [Theory]
    [InlineData("VOLUME [\"src\" \"dest\"]\n")]
    [InlineData("COPY [\"src\" \"dest\"]\n")]
    [InlineData("ADD [\"src\" \"dest\"]\n")]
    [InlineData("VOLUME [\"src\"] trailing\n")]
    [InlineData("COPY [\"src\"] trailing\n")]
    [InlineData("ADD [\"src\"] trailing\n")]
    public void InvalidJsonListUsesOperandFallback(string text)
    {
        Instruction standalone = ParseListInstruction(text);
        Assert.Equal(text, standalone.ToString());
        Assert.DoesNotContain(standalone.Tokens, token => token is SymbolToken);
        Assert.Equal(text, Dockerfile.Parse(text).ToString());
        DockerfileParseResult result = Dockerfile.TryParse(text);
        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(text, result.Dockerfile!.ToString());
    }

    [Theory]
    [InlineData("VOLUME [\"src\", \"dest\"]\n")]
    [InlineData("COPY [\"src\", \"dest\"]\n")]
    [InlineData("ADD [\"src\", \"dest\"]\n")]
    [InlineData("SHELL [\"src\", \"dest\"]\n")]
    public void ValidJsonListKeepsJsonTokens(string text)
    {
        Instruction standalone = ParseListInstruction(text);
        Assert.Equal(text, standalone.ToString());
        Assert.Contains(standalone is CommandInstruction command ? command.Command!.Tokens : standalone.Tokens,
            token => token is SymbolToken);
        Assert.Equal(text, Dockerfile.Parse(text).ToString());
        DockerfileParseResult result = Dockerfile.TryParse(text);
        Assert.True(result.Success);
        Assert.Equal(text, result.Dockerfile!.ToString());
    }

    [Theory]
    [InlineData("VOLUME [\"src\", 1]\n")]
    [InlineData("COPY [\"src\", 1]\n")]
    [InlineData("ADD [\"src\", 1]\n")]
    [InlineData("SHELL [\"src\", 1]\n")]
    public void JsonListWithNonStringElementsRejects(string text)
    {
        Assert.Throws<ParseException>(() => ParseListInstruction(text));
        Assert.Throws<ParseException>(() => Dockerfile.Parse(text));
        Assert.False(Dockerfile.TryParse(text).Success);
    }

    [Fact]
    public void BacktickEscapedQuoteIsNotAJsonEscape()
    {
        string text = "RUN [\"echo he`\"llo\"]\n";
        RunInstruction standalone = RunInstruction.Parse(text, '`');
        Assert.Equal(text, standalone.ToString());
        Assert.IsType<ShellFormCommand>(standalone.Command);
        string document = "# escape=`\n" + text;
        Assert.Equal(document, Dockerfile.Parse(document).ToString());
        DockerfileParseResult result = Dockerfile.TryParse(document);
        Assert.True(result.Success);
        Assert.IsType<ShellFormCommand>(result.Dockerfile!.Items.OfType<RunInstruction>().Single().Command);
    }

    private static Instruction ParseListInstruction(string text) => text.Split(' ')[0] switch
    {
        "VOLUME" => VolumeInstruction.Parse(text),
        "COPY" => CopyInstruction.Parse(text),
        "ADD" => AddInstruction.Parse(text),
        "SHELL" => ShellInstruction.Parse(text),
        _ => throw new ArgumentException(nameof(text))
    };

    private static Instruction ParseInstruction(string text) => text.Split(' ')[0] switch
    {
        "RUN" => RunInstruction.Parse(text),
        "CMD" => CmdInstruction.Parse(text),
        "ENTRYPOINT" => EntrypointInstruction.Parse(text),
        "HEALTHCHECK" => HealthCheckInstruction.Parse(text),
        "ONBUILD" => OnBuildInstruction.Parse(text),
        _ => throw new ArgumentException(nameof(text))
    };

    private static Command? GetCommand(Instruction instruction) => instruction switch
    {
        CommandInstruction command => command.Command,
        HealthCheckInstruction healthCheck => healthCheck.Command,
        OnBuildInstruction onBuild => GetCommand(onBuild.Instruction),
        _ => throw new ArgumentException(nameof(instruction))
    };
}
