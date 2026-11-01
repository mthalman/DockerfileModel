using Valleysoft.DockerfileModel.Parsing;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class CommandJsonRecognitionTests
{
    [Theory]
    [InlineData("RUN")]
    [InlineData("CMD")]
    [InlineData("ENTRYPOINT")]
    [InlineData("HEALTHCHECK")]
    [InlineData("SHELL")]
    public void ArgumentSequenceConstructorsRetainLegacyExecOperands(string kind)
    {
        Instruction instruction = kind switch
        {
            "RUN" => new RunInstruction(@"C:\work", new[] { "arg" }),
            "CMD" => new CmdInstruction(@"C:\work", new[] { "arg" }),
            "ENTRYPOINT" => new EntrypointInstruction(@"C:\work", new[] { "arg" }),
            "HEALTHCHECK" => new HealthCheckInstruction(@"C:\work", new[] { "arg" }, interval: "5s"),
            "SHELL" => new ShellInstruction(@"C:\work", new[] { "arg" }),
            _ => throw new ArgumentException(nameof(kind))
        };
        ExecFormCommand command = Assert.IsType<ExecFormCommand>(GetCommand(instruction));
        Assert.Equal(new[] { @"C:\work", "arg" }, command.Values);
        Assert.Equal("[\"C:\\work\", \"arg\"]", command.ToString());
        if (kind == "SHELL")
        {
            Assert.Throws<ParseException>(() => ParseInstruction(instruction.ToString()));
        }
        else
        {
            Assert.IsType<ShellFormCommand>(GetCommand(ParseInstruction(instruction.ToString())));
        }

        command.Values.Add("extra");

        Assert.Equal(new[] { @"C:\work", "arg", "extra" }, command.Values);
        if (instruction is HealthCheckInstruction healthCheck)
        {
            Assert.Equal("5s", healthCheck.Interval);
        }
    }

    [Theory]
    [InlineData("CMD")]
    [InlineData("ENTRYPOINT")]
    [InlineData("HEALTHCHECK")]
    public void DefaultArgumentConstructorsRetainLegacyExecOperands(string kind)
    {
        string[] values = { @"C:\work", "arg" };
        Instruction instruction = kind switch
        {
            "CMD" => new CmdInstruction(values),
            "ENTRYPOINT" => new EntrypointInstruction(values),
            "HEALTHCHECK" => new HealthCheckInstruction(values),
            _ => throw new ArgumentException(nameof(kind))
        };
        Assert.Equal(values, Assert.IsType<ExecFormCommand>(GetCommand(instruction)).Values);
    }

    [Fact]
    public void ExecConstructorRetainsRunFlagsAndShellOnlyCommand()
    {
        RunInstruction run = new(@"C:\work", new[] { "arg" }, Array.Empty<Mount>(),
            network: "none", security: "sandbox");
        Assert.Equal("none", run.Network);
        Assert.Equal("sandbox", run.Security);
        Assert.Equal(new[] { @"C:\work", "arg" }, Assert.IsType<ExecFormCommand>(run.Command).Values);
        Assert.Equal(new[] { @"C:\work" },
            Assert.IsType<ExecFormCommand>(new ShellInstruction(@"C:\work").Command).Values);
    }

    [Theory]
    [InlineData("RUN")]
    [InlineData("CMD")]
    [InlineData("ENTRYPOINT")]
    [InlineData("HEALTHCHECK")]
    [InlineData("SHELL")]
    public void ArgumentSequenceConstructorsKeepSupportedJsonStringSyntax(string kind)
    {
        const string value = "a\\\"b`";
        Instruction instruction = kind switch
        {
            "RUN" => new RunInstruction(value, new[] { "arg" }, '`'),
            "CMD" => new CmdInstruction(value, new[] { "arg" }, '`'),
            "ENTRYPOINT" => new EntrypointInstruction(value, new[] { "arg" }, '`'),
            "HEALTHCHECK" => new HealthCheckInstruction(value, new[] { "arg" }, escapeChar: '`'),
            "SHELL" => new ShellInstruction(value, new[] { "arg" }, '`'),
            _ => throw new ArgumentException(nameof(kind))
        };
        ExecFormCommand command = Assert.IsType<ExecFormCommand>(GetCommand(instruction));
        Assert.Equal(new[] { value, "arg" }, command.Values);
        string before = instruction.ToString();
        command.ValueTokens[0].Value = command.ValueTokens[0].Value;
        Assert.Equal(before, instruction.ToString());
        command.Values.Add("extra");
        Assert.Equal(new[] { value, "arg", "extra" }, command.Values);
    }

    [Fact]
    public void ExplicitExecFormRetainsMultilineJsonWhitespace()
    {
        const string text = "[ \n\t\n\"a\", \n\n\"b\"\n\n]";
        ExecFormCommand command = ExecFormCommand.Parse(text);
        Assert.Equal(text, command.ToString());
        Assert.Equal(new[] { "a", "b" }, command.Values);
    }

    [Theory]
    [InlineData("self-replace")]
    [InlineData("move")]
    [InlineData("remove")]
    public void EditsWithoutAdoptionDoNotReselectLiveJsonOperands(string operation)
    {
        ExecFormCommand command = Assert.IsType<ExecFormCommand>(RunInstruction.Parse("RUN [\"old\", \"other\"]").Command);
        LiteralToken original = command.ValueTokens[0];
        int liveSelections = 0;
        InstructionTokenListAdapter<LiteralToken> adapter = new(command, tokens =>
        {
            if (tokens.Any(token => ReferenceEquals(token, original)))
            {
                liveSelections++;
            }
            return tokens;
        });
        switch (operation)
        {
            case "self-replace":
                adapter.Replace(0, original, TriviaDisposition.Preserve);
                break;
            case "move":
                adapter.Move(0, 1);
                break;
            case "remove":
                adapter.Remove(new[] { 1 }, TriviaDisposition.Discard);
                break;
            default:
                throw new ArgumentException(nameof(operation));
        }
        Assert.Equal(1, liveSelections);
        Assert.Contains(original, command.ValueTokens);
        Assert.True(original.UseJsonStringSyntax);
    }

    public static IEnumerable<object[]> JsonCommandEditCases()
    {
        foreach (string prefix in new[] { "RUN", "CMD", "ENTRYPOINT", "HEALTHCHECK CMD", "ONBUILD RUN", "SHELL" })
        {
            yield return new object[] { prefix, "[\"\\\necho\", \"x\"]", '\\' };
            yield return new object[] { prefix, "[\"\\\n\", \"x\"]", '\\' };
            yield return new object[] { prefix, "[\"echo`\", \"x\"]", '`' };
            yield return new object[] { prefix, "[\"a\\\"b\", \"x\"]", '`' };
        }
    }

    [Theory]
    [MemberData(nameof(JsonCommandEditCases))]
    public void ParsedJsonCommandsSupportCollectionEdits(string prefix, string arguments, char escapeChar)
    {
        ExecFormCommand command = Assert.IsType<ExecFormCommand>(
            GetCommand(ParseInstruction(prefix + " " + arguments + "\n", escapeChar)));
        LiteralToken original = command.ValueTokens[0];
        string body = original.ToString();
        string value = original.Value;

        command.Values.Add("y");

        Assert.Equal(new[] { value, "x", "y" }, command.Values);
        Assert.Same(original, command.ValueTokens[0]);
        Assert.Equal(body, original.ToString());
        string before = command.ToString();
        command.ValueTokens.Replace(0, original);
        Assert.Equal(before, command.ToString());
        command.ValueTokens.Move(0, 1);
        Assert.Equal(new[] { "x", value, "y" }, command.Values);
        Assert.Equal(body, original.ToString());
        command.ValueTokens.RemoveAt(1, TriviaDisposition.Discard);
        Assert.Equal(new[] { "x", "y" }, command.Values);
    }

    [Theory]
    [InlineData("RUN")]
    [InlineData("CMD")]
    [InlineData("ENTRYPOINT")]
    [InlineData("HEALTHCHECK CMD")]
    [InlineData("ONBUILD RUN")]
    [InlineData("SHELL")]
    [InlineData("COPY")]
    [InlineData("ADD")]
    [InlineData("VOLUME")]
    public void ParsedJsonLiteralUpdatesRetainJsonEscapeSyntax(string prefix)
    {
        string text = prefix + " [\"echo`\", \"dest\"]\n";
        Instruction instruction = ParseInstruction(text, '`');
        IEnumerable<Token> tokens = instruction is FileTransferInstruction or VolumeInstruction
            ? instruction.Tokens
            : Assert.IsType<ExecFormCommand>(GetCommand(instruction)).Tokens;
        LiteralToken literal = tokens.OfType<LiteralToken>().First();

        literal.Value = literal.Value;

        Assert.Equal(text, instruction.ToString());
        literal.Value = "a\\\"b`";
        Assert.Equal("a\\\"b`", literal.Value);
        Assert.Equal(prefix + " [\"a\\\"b`\", \"dest\"]\n", instruction.ToString());
    }

    [Theory]
    [InlineData("RUN")]
    [InlineData("COPY")]
    [InlineData("ADD")]
    [InlineData("VOLUME")]
    public void AdoptedJsonLiteralsRetainTheirGrammarForLaterValueUpdates(string prefix)
    {
        Instruction instruction = ParseInstruction(prefix + " [\"old\", \"dest\"]", '`');
        EditableList<LiteralToken> tokens = instruction switch
        {
            FileTransferInstruction transfer => transfer.SourceTokens,
            VolumeInstruction volume => volume.PathTokens,
            _ => Assert.IsType<ExecFormCommand>(GetCommand(instruction)).ValueTokens
        };
        LiteralToken incoming = new(new Token[] { new StringToken("incoming`") }, false, '`');

        tokens.Add(incoming);
        string before = instruction.ToString();
        incoming.Value = incoming.Value;

        Assert.Contains(incoming, tokens);
        Assert.Equal(before, instruction.ToString());
        Assert.Throws<ParseException>(() => incoming.Value = "unescaped\"quote");
        Assert.Equal(before, instruction.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyCommandAdoptionDoesNotDowngradeSharedJsonLiteralGrammar(bool replace)
    {
        ExecFormCommand donor = Assert.IsType<ExecFormCommand>(
            RunInstruction.Parse("RUN [\"echo\"]", '`').Command);
        LiteralToken shared = donor.ValueTokens[0];
        ExecFormCommand receiver = ExecFormCommand.Parse("[\"other\"]", '`');

        if (replace)
        {
            receiver.ValueTokens.Replace(0, shared);
        }
        else
        {
            receiver.ValueTokens.Add(shared);
        }

        Assert.Same(shared, donor.ValueTokens[0]);
        Assert.Same(shared, receiver.ValueTokens[replace ? 0 : 1]);
        Assert.True(shared.UseJsonStringSyntax);
        Assert.False(receiver.UseJsonStringSyntax);
        shared.Value = "echo`";
        Assert.Equal("echo`", shared.Value);
        Assert.Equal("[\"echo`\"]", donor.ToString());
        Assert.Equal(replace ? "[\"echo`\"]" : "[\"other\", \"echo`\"]", receiver.ToString());
    }

    [Fact]
    public void LegacyCommandAdoptionKeepsLegacyLiteralGrammar()
    {
        ExecFormCommand receiver = ExecFormCommand.Parse("[\"other\"]", '`');
        LiteralToken incoming = new("echo", escapeChar: '`');

        receiver.ValueTokens.Add(incoming);

        Assert.False(incoming.UseJsonStringSyntax);
        Assert.False(receiver.UseJsonStringSyntax);
    }

    [Fact]
    public void JsonStringParsingReusesContinuationParsers()
    {
        var parser = CommandParsers.JsonStringTokens('`', canContainVariables: false)
            .Try().Many().Flatten().AtEnd();
        string text = string.Concat(Enumerable.Repeat("a\\n ", 100));
        for (int i = 0; i < 10; i++)
        {
            parser.Parse(text);
        }
        long start = GC.GetAllocatedBytesForCurrentThread();
        parser.Parse(text);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.True(allocated < 1_000_000, $"Parsing 100 escaped segments allocated {allocated} bytes.");
    }

    [Fact]
    public void JsonArrayParsingReusesElementParsers()
    {
        var strictParser = CommandParsers.JsonArray('\\', canContainVariables: true, allowEmpty: true,
            useJsonStringSyntax: true);
        var legacyParser = CommandParsers.JsonArray('\\', canContainVariables: true, allowEmpty: true);
        TextSpan input = new("[]");
        for (int i = 0; i < 20; i++)
        {
            Assert.True(strictParser(input).HasValue);
            Assert.True(legacyParser(input).HasValue);
        }
        long strict = Allocated(strictParser, input);
        long legacy = Allocated(legacyParser, input);
        Assert.True(strict < 500_000, $"Strict grammar allocated {strict} bytes for 100 empty arrays.");
        Assert.True(legacy < 500_000, $"Legacy grammar allocated {legacy} bytes for 100 empty arrays.");

        static long Allocated(TextParser<IEnumerable<Token>> parser, TextSpan input)
        {
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                parser(input);
            }
            return GC.GetAllocatedBytesForCurrentThread() - start;
        }
    }

    public static IEnumerable<object[]> JsonStringSyntaxCases()
    {
        (string Arguments, char EscapeChar, string[] Values)[] cases =
        {
            ("[\"a\\\"b\", \"dest\"]", '`', new[] { "a\\\"b", "dest" }),
            ("[\"echo`\", \"dest\"]", '`', new[] { "echo`", "dest" }),
            ("[\"\\\necho\", \"dest\"]", '\\', new[] { "echo", "dest" }),
            ("[\"\\\n\", \"dest\"]", '\\', new[] { "", "dest" }),
            ("[\"`\necho\", \"dest\"]", '`', new[] { "echo", "dest" }),
            ("[\"`\n\", \"dest\"]", '`', new[] { "", "dest" }),
            ("[\"\\\n# comment\n echo\", \"dest\"]", '\\', new[] { " echo", "dest" }),
            ("[\"\\\r\n\\\r\necho\", \"dest\"]", '\\', new[] { "echo", "dest" }),
            ("[\"ec`\nho\\\"there\", \"dest\"]", '`', new[] { "echo\\\"there", "dest" }),
            ("[\"a\\`\n\"b\", \"dest\"]", '`', new[] { "a\\\"b", "dest" }),
            ("[\"a\\`\n# comment\n\"b\", \"dest\"]", '`', new[] { "a\\\"b", "dest" }),
            ("[\"a\\`\n # comment\n\\b\", \"dest\"]", '`', new[] { "a\\\\b", "dest" }),
            ("[\"a\\`\r\n# comment\r\nnb\", \"dest\"]", '`', new[] { "a\\nb", "dest" }),
            ("[\"a\\`\n# comment\nu0020b\", \"dest\"]", '`', new[] { "a\\u0020b", "dest" }),
            ("[\"a\\\\b\\t\\u0020\", \"dest\"]", '`', new[] { "a\\\\b\\t\\u0020", "dest" })
        };
        foreach (string prefix in new[] { "RUN", "CMD", "ENTRYPOINT", "HEALTHCHECK CMD", "ONBUILD RUN",
            "SHELL", "ADD", "COPY", "VOLUME" })
        {
            foreach (var scenario in cases)
            {
                yield return new object[] { prefix, scenario.Arguments, scenario.EscapeChar, scenario.Values };
            }
        }
    }

    public static IEnumerable<object[]> JsonContinuationTriviaCases()
    {
        (string Arguments, char EscapeChar, string[] Values)[] cases =
        {
            ("[\\\n\n\n\"a\", \"dest\"]", '\\', new[] { "a", "dest" }),
            ("[\"a\", \\\n\n\n\"dest\"]", '\\', new[] { "a", "dest" }),
            ("[\"a\"\\\n\n\n, \"dest\"]", '\\', new[] { "a", "dest" }),
            ("[\"a\", \"dest\"\\\n\n\n]", '\\', new[] { "a", "dest" }),
            ("[\"a\\\n\n\n# comment\nb\", \"dest\"]", '\\', new[] { "ab", "dest" }),
            ("[\"a\\`\n \t\n\n # first\n \n# second\n\"b\", \"dest\"]", '`', new[] { "a\\\"b", "dest" }),
            ("[\"a\\`\r\n\r\n # first\r\n`\r\n\r\n# second\r\nnb\", \"dest\"]", '`', new[] { "a\\nb", "dest" }),
            ("[\"a`\r\n\r\n\r\n # first\r\n\r\n# second\r\n b\", \"dest\"]", '`', new[] { "a b", "dest" })
        };
        foreach (string prefix in new[] { "RUN", "CMD", "ENTRYPOINT", "HEALTHCHECK CMD", "ONBUILD RUN",
            "SHELL", "ADD", "COPY", "VOLUME" })
        {
            foreach (var scenario in cases)
            {
                yield return new object[] { prefix, scenario.Arguments, scenario.EscapeChar, scenario.Values };
            }
        }
    }

    [Theory]
    [MemberData(nameof(JsonContinuationTriviaCases))]
    public void ContinuedJsonRetainsSkippedTrivia(string prefix, string arguments, char escapeChar, string[] values)
    {
        string text = prefix + " " + arguments + "\n";
        AssertInstruction(ParseInstruction(text, escapeChar));
        string documentText = (escapeChar == '`' ? "# escape=`\n" : "") + text + "RUN echo after\n";
        foreach (DockerfileParseMode mode in Enum.GetValues<DockerfileParseMode>())
        {
            DockerfileParseResult result = Dockerfile.TryParse(documentText, new DockerfileParseOptions { Mode = mode });
            Assert.True(result.Success);
            Assert.Empty(result.Diagnostics);
            Assert.Equal(documentText, result.Dockerfile!.ToString());
            AssertInstruction(result.Dockerfile.Items.OfType<Instruction>().First());
            Assert.IsType<RunInstruction>(result.Dockerfile.Items.OfType<Instruction>().Last());
            SourceSpanTests.AssertPartition(documentText, result.Dockerfile);
        }

        void AssertInstruction(Instruction instruction)
        {
            Assert.Equal(text, instruction.ToString());
            IEnumerable<Token> tokens = instruction is FileTransferInstruction or VolumeInstruction
                ? instruction.Tokens
                : Assert.IsType<ExecFormCommand>(GetCommand(instruction)).Tokens;
            Assert.Equal(values, tokens.OfType<LiteralToken>().Select(token => token.Value));
        }
    }

    [Theory]
    [InlineData("RUN", false)]
    [InlineData("CMD", false)]
    [InlineData("ENTRYPOINT", false)]
    [InlineData("HEALTHCHECK CMD", false)]
    [InlineData("ONBUILD RUN", false)]
    [InlineData("SHELL", false)]
    [InlineData("ADD", true)]
    [InlineData("COPY", true)]
    [InlineData("VOLUME", false)]
    public void JsonStringsRetainInstructionVariableRecognitionPolicy(string prefix, bool recognizeVariables)
    {
        string text = prefix + " [\"$SRC`\", \"${DST:-default}\\\"tail\"]\n";
        Instruction instruction = ParseInstruction(text, '`');
        Assert.Equal(text, instruction.ToString());
        IEnumerable<Token> tokens = instruction is FileTransferInstruction or VolumeInstruction
            ? instruction.Tokens
            : Assert.IsType<ExecFormCommand>(GetCommand(instruction)).Tokens;
        LiteralToken[] literals = tokens.OfType<LiteralToken>().ToArray();
        Assert.Equal(new[] { "$SRC`", "${DST:-default}\\\"tail" }, literals.Select(token => token.Value));
        Assert.Equal(recognizeVariables ? 2 : 0,
            literals.SelectMany(token => token.Tokens).OfType<VariableRefToken>().Count());
        foreach (LiteralToken literal in literals)
        {
            literal.Value = literal.Value;
        }
        Assert.Equal(text, instruction.ToString());
        Assert.Equal(recognizeVariables ? 2 : 0,
            literals.SelectMany(token => token.Tokens).OfType<VariableRefToken>().Count());
    }

    [Theory]
    [MemberData(nameof(JsonStringSyntaxCases))]
    public void JsonStringsKeepJsonEscapesAndDockerfileContinuations(
        string prefix, string arguments, char escapeChar, string[] values)
    {
        string text = prefix + " " + arguments + "\n";
        AssertInstruction(ParseInstruction(text, escapeChar));
        string documentText = (escapeChar == '`' ? "# escape=`\n" : "") + text + "RUN echo after\n";
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
            AssertInstruction(document.Items.OfType<Instruction>().First());
            Assert.IsType<RunInstruction>(document.Items.OfType<Instruction>().Last());
        }

        void AssertInstruction(Instruction instruction)
        {
            Assert.Equal(text, instruction.ToString());
            IEnumerable<Token> tokens = instruction is FileTransferInstruction or VolumeInstruction
                ? instruction.Tokens
                : Assert.IsType<ExecFormCommand>(GetCommand(instruction)).Tokens;
            Assert.Contains(tokens, token => token is SymbolToken);
            Assert.Equal(values, tokens.OfType<LiteralToken>().Select(token => token.Value));
        }
    }

    [Theory]
    [InlineData("RUN")]
    [InlineData("CMD")]
    [InlineData("ENTRYPOINT")]
    [InlineData("HEALTHCHECK CMD")]
    [InlineData("ONBUILD RUN")]
    [InlineData("SHELL")]
    public void JsonEscapesRetainContinuedTriviaDuringCollectionEdits(string prefix)
    {
        const string arguments = "[\"a\\`\n\n # comment\n\"b\", \"dest\"]";
        ExecFormCommand command = Assert.IsType<ExecFormCommand>(
            GetCommand(ParseInstruction(prefix + " " + arguments, '`')));
        LiteralToken original = command.ValueTokens[0];
        string body = original.ToString();

        command.Values.Add("extra");

        Assert.Same(original, command.ValueTokens[0]);
        Assert.Equal(body, original.ToString());
        Assert.Equal(new[] { "a\\\"b", "dest", "extra" }, command.Values);
        original.Value += "c";
        Assert.Equal(new[] { "a\\\"bc", "dest", "extra" }, command.Values);
        Assert.Equal(command.ToString(), GetCommand(ParseInstruction(prefix + " " + command, '`'))!.ToString());
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void WhitespaceAfterJsonEscapeContinuationIsNotSkipped(string whitespace)
    {
        string text = "RUN [\"a\\`\n# comment\n" + whitespace + "nb\", \"dest\"]\n";
        RunInstruction instruction = RunInstruction.Parse(text, '`');
        Assert.IsType<ShellFormCommand>(instruction.Command);
        Assert.Equal(text, instruction.ToString());
    }

    [Theory]
    [InlineData("echo ", '\\')]
    [InlineData("  echo ", '\\')]
    [InlineData("\\\\server\\share ", '\\')]
    [InlineData("  \\echo ", '\\')]
    [InlineData("\\  echo ", '\\')]
    [InlineData("`echo ", '`')]
    [InlineData("  `  echo ", '`')]
    public void ShellRecognitionDoesNotAllocateProportionalToHeaderLength(string prefix, char escapeChar)
    {
        var parser = CommandParsers.MaybeJson(Superpower.Parse.Return(true), Superpower.Parse.Return(false), escapeChar);
        TextSpan input = new(prefix + new string('x', 100_000));
        for (int i = 0; i < 20; i++)
        {
            Assert.False(parser(input).Value);
        }

        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            parser(input);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.True(allocated < 1024, $"Recognition allocated {allocated} bytes for 100 shell headers.");
    }

    [Theory]
    [InlineData("[\"echo\"]", '\\')]
    [InlineData("  [\"echo\"]", '\\')]
    [InlineData("\\\n [\"echo\"]", '\\')]
    [InlineData("\\ \t\r\n [\"echo\"]", '\\')]
    [InlineData(" \\\n# comment\n [\"echo\"]", '\\')]
    [InlineData("`\n [\"echo\"]", '`')]
    [InlineData(" ` \t\r\n# comment\r\n [\"echo\"]", '`')]
    public void JsonRecognitionIncludesLeadingContinuations(string text, char escapeChar)
    {
        var parser = CommandParsers.MaybeJson(Superpower.Parse.Return(true), Superpower.Parse.Return(false), escapeChar);
        Assert.True(parser(new TextSpan("prefix" + text).Skip(6)).Value);
    }

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

    private static Instruction ParseInstruction(string text, char escapeChar = Dockerfile.DefaultEscapeChar) => text.Split(' ')[0] switch
    {
        "RUN" => RunInstruction.Parse(text, escapeChar),
        "CMD" => CmdInstruction.Parse(text, escapeChar),
        "ENTRYPOINT" => EntrypointInstruction.Parse(text, escapeChar),
        "HEALTHCHECK" => HealthCheckInstruction.Parse(text, escapeChar),
        "ONBUILD" => OnBuildInstruction.Parse(text, escapeChar),
        "SHELL" => ShellInstruction.Parse(text, escapeChar),
        "ADD" => AddInstruction.Parse(text, escapeChar),
        "COPY" => CopyInstruction.Parse(text, escapeChar),
        "VOLUME" => VolumeInstruction.Parse(text, escapeChar),
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
