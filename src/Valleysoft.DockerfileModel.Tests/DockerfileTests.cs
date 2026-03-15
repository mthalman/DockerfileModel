using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class DockerfileTests
{
    [Theory]
    [InlineData("# escape=`\nFROM scratch", '`')]
    [InlineData("# escape=\\\nFROM scratch", '\\')]
    [InlineData("FROM scratch", '\\')]
    public void EscapeChar(string content, char expectedEscapeChar)
    {
        Dockerfile dockerfile = Dockerfile.Parse(content);
        Assert.Equal(expectedEscapeChar, dockerfile.EscapeChar);
    }

    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<Dockerfile> scenario)
    {
        if (scenario.ParseExceptionPosition is null)
        {
            Dockerfile result = Dockerfile.Parse(scenario.Text);
            Assert.Equal(scenario.Text, result.ToString());
            Assert.Collection(result.Items, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }
        else
        {
            ParseException exception = Assert.Throws<ParseException>(
                () => Dockerfile.Parse(scenario.Text));
            Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
            Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
        }
    }

    [Fact]
    public void ResolveArgValues_SingleStage_LocalArg()
    {
        List<string> lines = new()
        {
            "# escape=`",
            "FROM image",
            "ARG test=a",
            "ARG x=$test-`$test"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables((Instruction)dockerfile.Items.Last());
        Assert.Equal("ARG x=a-`$test", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());

        dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true });

        Assert.Equal("ARG x=a-`$test", dockerfile.Items.Last().ToString());
    }

    [Fact]
    public void ResolveArgValues_SingleStage_LocalArg_Override()
    {
        List<string> lines = new()
        {
            "FROM image",
            "ARG test=a",
            "ARG x=$test-\\$test"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

        Dictionary<string, string> argValues = new()
        {
            { "test", "b" }
        };

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables((Instruction)dockerfile.Items.Last(), argValues);
        Assert.Equal("ARG x=b-\\$test", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());

        dockerfile.ResolveVariables(argValues, options: new ResolutionOptions { UpdateInline = true });

        Assert.Equal("ARG x=b-\\$test", dockerfile.Items.Last().ToString());
    }

    [Fact]
    public void ResolveArgValues_MultiStage_LocalArg()
    {
        List<string> lines = new()
        {
            "FROM image as stage1",
            "ARG test=a",
            "ARG x=$test",
            "FROM image2 as stage2",
            "ARG test=",
            "ARG y=$test-c",
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));
        StagesView stagesView = new(dockerfile);

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables((Instruction)stagesView.Stages.First().Items.Last());
        Assert.Equal("ARG x=a\n", resolvedVal);
        resolvedVal = dockerfile.ResolveVariables((Instruction)stagesView.Stages.Last().Items.Last());
        Assert.Equal("ARG y=-c", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());

        dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true });

        Assert.Equal("ARG x=a\n", stagesView.Stages.First().Items.Last().ToString());
        Assert.Equal("ARG y=-c", stagesView.Stages.Last().Items.Last().ToString());
    }

    [Fact]
    public void ResolveArgValues_MultiStage_LocalArg_Override()
    {
        List<string> lines = new()
        {
            "FROM image as stage1",
            "ARG test=a",
            "ARG x=$test",
            "FROM image2 as stage2",
            "ARG test=",
            "ARG y=$test-c",
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));
        StagesView stagesView = new(dockerfile);

        Dictionary<string, string> argValues = new()
        {
            { "test", "z" }
        };

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables(
            (Instruction)stagesView.Stages.First().Items.Last(), argValues);
        Assert.Equal("ARG x=z\n", resolvedVal);
        resolvedVal = dockerfile.ResolveVariables(
            (Instruction)stagesView.Stages.Last().Items.Last(), argValues);
        Assert.Equal("ARG y=z-c", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());

        dockerfile.ResolveVariables(argValues, options: new ResolutionOptions { UpdateInline = true });
            
        Assert.Equal("ARG x=z\n", stagesView.Stages.First().Items.Last().ToString());
        Assert.Equal("ARG y=z-c", stagesView.Stages.Last().Items.Last().ToString());
    }

    [Fact]
    public void ResolveArgValues_SingleStage_GlobalArg()
    {
        List<string> lines = new()
        {
            "ARG test1=a",
            "ARG test2=b",
            "ARG test3=c",
            "FROM image:$test1",
            "ARG test2",
            "ARG test3=c1",
            "ARG test4=d",
            "ARG x=$test1-$test2-$test3-$test4"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables(
            dockerfile.Items.OfType<FromInstruction>().First());
        Assert.Equal("FROM image:a\n", resolvedVal);
        resolvedVal = dockerfile.ResolveVariables(
            (Instruction)dockerfile.Items.Last());
        Assert.Equal("ARG x=-b-c1-d", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());

        dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true });

        Assert.Equal("FROM image:a\n", dockerfile.Items.OfType<FromInstruction>().First().ToString());
        Assert.Equal("ARG x=-b-c1-d", dockerfile.Items.Last().ToString());
    }

    [Fact]
    public void ResolveArgValues_SingleStage_GlobalArg_Override()
    {
        List<string> lines = new()
        {
            "ARG test1=a",
            "ARG test2=b",
            "ARG test3=c",
            "FROM image:$test1",
            "ARG test2",
            "ARG test3=c1",
            "ARG test4=d",
            "ARG x=$test1-$test2-$test3-$test4"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

        Dictionary<string, string> argValues = new()
        {
            { "test1", "a1" },
            { "test2", "b1" },
            { "test3", "c2" },
            { "test4", "d1" }
        };

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables(
            dockerfile.Items.OfType<FromInstruction>().First(), argValues);
        Assert.Equal("FROM image:a1\n", resolvedVal);
        resolvedVal = dockerfile.ResolveVariables(
            (Instruction)dockerfile.Items.Last(), argValues);
        Assert.Equal("ARG x=-b1-c2-d1", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());

        dockerfile.ResolveVariables(argValues, options: new ResolutionOptions { UpdateInline = true });

        Assert.Equal("FROM image:a1\n", dockerfile.Items.OfType<FromInstruction>().First().ToString());
        Assert.Equal("ARG x=-b1-c2-d1", dockerfile.Items.Last().ToString());
    }

    [Fact]
    public void ResolveArgValues_MultiStage_GlobalArg()
    {
        List<string> lines = new()
        {
            "ARG test1=a",
            "ARG test2",
            "ARG test3=c",
            "FROM image:$test1 as stage1",
            "ARG test2",
            "ARG test3=c1",
            "ARG test4=d",
            "ARG x=$test1-$test2-$test3-$test4",
            "FROM image:$test2 as stage2",
            "ARG test2",
            "ARG test3",
            "ARG test4=d",
            "ARG y=$test1-$test2-$test3-$test4"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));
        StagesView stagesView = new(dockerfile);

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables(
            stagesView.Stages.First().FromInstruction);
        Assert.Equal("FROM image:a as stage1\n", resolvedVal);
        resolvedVal = dockerfile.ResolveVariables(
            (Instruction)stagesView.Stages.First().Items.Last());
        Assert.Equal("ARG x=--c1-d\n", resolvedVal);
        resolvedVal = dockerfile.ResolveVariables(
            stagesView.Stages.Last().FromInstruction);
        Assert.Equal("FROM image: as stage2\n", resolvedVal);
        resolvedVal = dockerfile.ResolveVariables(
            (Instruction)stagesView.Stages.Last().Items.Last());
        Assert.Equal("ARG y=--c-d", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());

        dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true });

        Assert.Equal("FROM image:a as stage1\n", stagesView.Stages.First().FromInstruction.ToString());
        Assert.Equal("ARG x=--c1-d\n", stagesView.Stages.First().Items.Last().ToString());
        Assert.Equal("FROM image: as stage2\n", stagesView.Stages.Last().FromInstruction.ToString());
        Assert.Equal("ARG y=--c-d", stagesView.Stages.Last().Items.Last().ToString());
    }

    [Fact]
    public void ResolveArgValues_MultiStage_GlobalArg_Override()
    {
        List<string> lines = new()
        {
            "ARG test1=a",
            "ARG test2",
            "ARG test3=c",
            "FROM image:$test1 as stage1",
            "ARG test2",
            "ARG test3=c1",
            "ARG test4=d",
            "ARG x=$test1-$test2-$test3-$test4",
            "FROM image:$test2 as stage2",
            "ARG test2",
            "ARG test3",
            "ARG test4=d",
            "ARG y=$test1-$test2-$test3-$test4"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));
        StagesView stagesView = new(dockerfile);

        Dictionary<string, string> argValues = new()
        {
            { "test1", "a1" },
            { "test2", "b1" },
            { "test3", "c2" },
        };

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables(
            stagesView.Stages.First().FromInstruction, argValues);
        Assert.Equal("FROM image:a1 as stage1\n", resolvedVal);
        resolvedVal = dockerfile.ResolveVariables(
            (Instruction)stagesView.Stages.First().Items.Last(), argValues);
        Assert.Equal("ARG x=-b1-c2-d\n", resolvedVal);
        resolvedVal = dockerfile.ResolveVariables(
            stagesView.Stages.Last().FromInstruction, argValues);
        Assert.Equal("FROM image:b1 as stage2\n", resolvedVal);
        resolvedVal = dockerfile.ResolveVariables(
            (Instruction)stagesView.Stages.Last().Items.Last(), argValues);
        Assert.Equal("ARG y=-b1-c2-d", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());

        dockerfile.ResolveVariables(argValues, options: new ResolutionOptions { UpdateInline = true });

        Assert.Equal("FROM image:a1 as stage1\n", stagesView.Stages.First().FromInstruction.ToString());
        Assert.Equal("ARG x=-b1-c2-d\n", stagesView.Stages.First().Items.Last().ToString());
        Assert.Equal("FROM image:b1 as stage2\n", stagesView.Stages.Last().FromInstruction.ToString());
        Assert.Equal("ARG y=-b1-c2-d", stagesView.Stages.Last().Items.Last().ToString());
    }

    [Fact]
    public void ResolveArgValues_ArgOrder()
    {
        List<string> lines = new()
        {
            "FROM image",
            "ARG x=$test",
            "ARG test=a",
            "ARG y=$test"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

        Dictionary<string, string> argValues = new();

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables(
            (Instruction)dockerfile.Items.ElementAt(1), argValues);
        Assert.Equal("ARG x=\n", resolvedVal);
        resolvedVal = dockerfile.ResolveVariables(
            (Instruction)dockerfile.Items.Last(), argValues);
        Assert.Equal("ARG y=a", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());

        dockerfile.ResolveVariables(argValues, options: new ResolutionOptions { UpdateInline = true });

        Assert.Equal("ARG x=\n", dockerfile.Items.ElementAt(1).ToString());
        Assert.Equal("ARG y=a", dockerfile.Items.Last().ToString());
    }

    [Fact]
    public void ResolveArgValues_LocalOverride()
    {
        List<string> lines = new()
        {
            "ARG test=a",
            "FROM image",
            "ARG test",
            "ARG x=$test",
            "ARG test=b",
            "ARG y=$test"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables(
            (Instruction)dockerfile.Items.ElementAt(3));
        Assert.Equal("ARG x=a\n", resolvedVal);
        resolvedVal = dockerfile.ResolveVariables(
            (Instruction)dockerfile.Items.ElementAt(5));
        Assert.Equal("ARG y=b", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());

        dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true });

        Assert.Equal("ARG x=a\n", dockerfile.Items.ElementAt(3).ToString());
        Assert.Equal("ARG y=b", dockerfile.Items.ElementAt(5).ToString());
    }

    [Fact]
    public void ResolveArgValues_DependentArgs()
    {
        List<string> lines = new()
        {
            "ARG test1=a",
            "ARG test2=$test1",
            "ARG test3=${test2}",
            "FROM image:$test3"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));
        dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true });

        Assert.Equal("ARG test2=a\n", dockerfile.Items.ElementAt(1).ToString());
        Assert.Equal("ARG test3=a\n", dockerfile.Items.ElementAt(2).ToString());
        Assert.Equal("FROM image:a", dockerfile.Items.ElementAt(3).ToString());
    }

    [Fact]
    public void ResolveArgValues_DependentArgs_Overriden()
    {
        List<string> lines = new()
        {
            "ARG test1",
            "ARG test2=$test1",
            "ARG test3=${test1+bar}",
            "FROM image:$test2",
            "FROM image:$test3"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

        Dictionary<string, string> argValues = new()
        {
            { "test1", "foo" }
        };
        dockerfile.ResolveVariables(argValues, options: new ResolutionOptions { UpdateInline = true });

        Assert.Equal("ARG test2=foo\n", dockerfile.Items.ElementAt(1).ToString());
        Assert.Equal("ARG test3=bar\n", dockerfile.Items.ElementAt(2).ToString());
        Assert.Equal("FROM image:foo\n", dockerfile.Items.ElementAt(3).ToString());
        Assert.Equal("FROM image:bar", dockerfile.Items.ElementAt(4).ToString());
    }

    [Fact]
    public void ResolveArgValues_UndeclaredArg()
    {
        List<string> lines = new()
        {
            "FROM image",
            "ARG x=$test"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

        Dictionary<string, string> argValues = new()
        {
            { "test", "foo" }
        };

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables(
            (Instruction)dockerfile.Items.Last(), argValues);
        Assert.Equal("ARG x=", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());

        dockerfile.ResolveVariables(argValues, options: new ResolutionOptions { UpdateInline = true });

        Assert.Equal("ARG x=", dockerfile.Items.Last().ToString());
    }

    [Fact]
    public void ResolveArgValues_OverrideAsUnset()
    {
        List<string> lines = new()
        {
            "ARG test=foo",
            "FROM image",
            "ARG x=$test"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

        Dictionary<string, string> argValues = new()
        {
            { "test", null }
        };

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables(
            (Instruction)dockerfile.Items.Last(), argValues);
        Assert.Equal("ARG x=", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());

        dockerfile.ResolveVariables(argValues, options: new ResolutionOptions { UpdateInline = true });

        Assert.Equal("ARG x=", dockerfile.Items.Last().ToString());
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<Dockerfile>[] testInputs = new ParseTestScenario<Dockerfile>[]
        {
            new ParseTestScenario<Dockerfile>
            {
                Text = "from scratch",
                TokenValidators = new Action<Token>[]
                {
                    line => ValidateAggregate<FromInstruction>(line, "from scratch",
                        token => ValidateKeyword(token, "from"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "scratch"))
                }
            },
            new ParseTestScenario<Dockerfile>
            {
                Text = "F\\\nRO\\\nM \\\nscratch",
                TokenValidators = new Action<Token>[]
                {
                    line => ValidateAggregate<FromInstruction>(line, "F\\\nRO\\\nM \\\nscratch",
                        token => ValidateAggregate<KeywordToken>(token, "F\\\nRO\\\nM",
                            token => ValidateString(token, "F"),
                            token => ValidateAggregate<LineContinuationToken>(token, "\\\n",
                                token => ValidateSymbol(token, '\\'),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateString(token, "RO"),
                            token => ValidateAggregate<LineContinuationToken>(token, "\\\n",
                                token => ValidateSymbol(token, '\\'),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateString(token, "M")),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "\\\n",
                            token => ValidateSymbol(token, '\\'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateLiteral(token, "scratch"))
                }
            },
            new ParseTestScenario<Dockerfile>
            {
                Text = "FROM scratch",
                TokenValidators = new Action<Token>[]
                {
                    line => ValidateAggregate<FromInstruction>(line, "FROM scratch",
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "scratch"))
                }
            },
            new ParseTestScenario<Dockerfile>
            {
                Text = "FROM \\\r\nscratch",
                TokenValidators = new Action<Token>[]
                {
                    line => ValidateAggregate<FromInstruction>(line, "FROM \\\r\nscratch",
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "\\\r\n",
                            token => ValidateSymbol(token, '\\'),
                            token => ValidateNewLine(token, "\r\n")),
                        token => ValidateLiteral(token, "scratch"))
                }
            },
            new ParseTestScenario<Dockerfile>
            {
                Text = $"# escape=`\nFROM `\r\n  scratch",
                TokenValidators = new Action<Token>[]
                {
                    line => ValidateAggregate<ParserDirective>(line, "# escape=`\n"),
                    line => ValidateAggregate<FromInstruction>(line, $"FROM `\r\n  scratch",
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "`\r\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\r\n")),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateLiteral(token, "scratch"))
                }
            },
            new ParseTestScenario<Dockerfile>
            {
                Text = $"#comment\r\nFROM scratch",
                TokenValidators = new Action<Token>[]
                {
                    line => ValidateAggregate<Comment>(line, "#comment\r\n"),
                    line => ValidateAggregate<FromInstruction>(line, "FROM scratch")
                }
            },
            new ParseTestScenario<Dockerfile>
            {
                Text = $"\r\nFROM scratch\n# test\nRUN foo=\"bar\"\n",
                TokenValidators = new Action<Token>[]
                {
                    line => ValidateAggregate<Whitespace>(line, "\r\n"),
                    line => ValidateAggregate<FromInstruction>(line, "FROM scratch\n"),
                    line => ValidateAggregate<Comment>(line, "# test\n"),
                    line => ValidateAggregate<RunInstruction>(line, "RUN foo=\"bar\"\n"),
                }
            },
            new ParseTestScenario<Dockerfile>
            {
                Text = $"FROM \\\n#comment\nscratch",
                TokenValidators = new Action<Token>[]
                {
                    line => ValidateAggregate<FromInstruction>(line, "FROM \\\n#comment\nscratch",
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "\\\n",
                            token => ValidateSymbol(token, '\\'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateAggregate<CommentToken>(token, "#comment\n",
                            token => ValidateSymbol(token, '#'),
                            token => ValidateString(token, "comment"),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateLiteral(token, "scratch")
                    ),
                }
            },
            new ParseTestScenario<Dockerfile>
            {
                Text = $"RUN apt-get update \\\r\n  && apt-get install curl\r\n\r\n#testing",
                TokenValidators = new Action<Token>[]
                {
                    line => ValidateAggregate<RunInstruction>(line, "RUN apt-get update \\\r\n  && apt-get install curl\r\n",
                        new Action<Token>[]
                        {
                            token => ValidateKeyword(token, "RUN"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateAggregate<ShellFormCommand>(token, "apt-get update \\\r\n  && apt-get install curl\r\n",
                                token => ValidateAggregate<LiteralToken>(token, "apt-get update \\\r\n  && apt-get install curl\r\n",
                                    token => ValidateString(token, "apt-get update "),
                                    token => ValidateAggregate<LineContinuationToken>(token, "\\\r\n",
                                        token => ValidateSymbol(token, '\\'),
                                        token => ValidateNewLine(token, "\r\n")),
                                    token => ValidateString(token, "  && apt-get install curl"),
                                    token => ValidateNewLine(token, "\r\n"))),
                        }),
                    line => ValidateAggregate<Whitespace>(line, "\r\n"),
                    line => ValidateAggregate<Comment>(line, "#testing")
                }
            },
            new ParseTestScenario<Dockerfile>
            {
                Text = $"RUN apk add \\\n  userspace-rcu",
                TokenValidators = new Action<Token>[]
                {
                    line => ValidateAggregate<RunInstruction>(line, "RUN apk add \\\n  userspace-rcu",
                        new Action<Token>[]
                        {
                            token => ValidateKeyword(token, "RUN"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateAggregate<ShellFormCommand>(token, "apk add \\\n  userspace-rcu",
                                token => ValidateAggregate<LiteralToken>(token, "apk add \\\n  userspace-rcu",
                                    token => ValidateString(token, "apk add "),
                                    token => ValidateAggregate<LineContinuationToken>(token, "\\\n",
                                        token => ValidateSymbol(token, '\\'),
                                        token => ValidateNewLine(token, "\n")),
                                    token => ValidateString(token, "  userspace-rcu"))),
                        })
                }
            },
            new ParseTestScenario<Dockerfile>
            {
                Text = $"ENV \\\t \n VAR=VAL",
                TokenValidators = new Action<Token>[]
                {
                    line => ValidateAggregate<EnvInstruction>(line, "ENV \\\t \n VAR=VAL",
                        new Action<Token>[]
                        {
                            token => ValidateKeyword(token, "ENV"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateAggregate<LineContinuationToken>(token, "\\\t \n",
                                token => ValidateSymbol(token, '\\'),
                                token => ValidateWhitespace(token, "\t "),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "VAR=VAL",
                                token => ValidateIdentifier<Variable>(token, "VAR"),
                                token => ValidateSymbol(token, '='),
                                token => ValidateLiteral(token, "VAL"))
                        })
                }
            },
            new ParseTestScenario<Dockerfile>
            {
                Text = $"CMD echo hello",
                TokenValidators = new Action<Token>[]
                {
                    line => ValidateAggregate<CmdInstruction>(line, "CMD echo hello", new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "CMD"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                            token => ValidateLiteral(token, "echo hello"))
                    })
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    [Fact]
    public void Dockerfile_OnlyCommentsAndWhitespace_ParsesSuccessfully()
    {
        // A Dockerfile with no instructions at all — just comments and blank lines
        string text = "# comment\n\n# another comment\n";
        Dockerfile df = Dockerfile.Parse(text);
        Assert.NotNull(df);
        Assert.Equal(text, df.ToString());
    }

    [Fact]
    public void Dockerfile_SingleFromOnly_RoundTrips()
    {
        string text = "FROM alpine\n";
        Dockerfile df = Dockerfile.Parse(text);
        Assert.Equal(text, df.ToString());
    }

    [Fact]
    public void Dockerfile_CRLF_RoundTrips()
    {
        string text = "FROM alpine\r\nRUN echo hello\r\n";
        Dockerfile df = Dockerfile.Parse(text);
        Assert.Equal(text, df.ToString());
    }

    [Fact]
    public void Dockerfile_MixedLineEndings_RoundTrips()
    {
        // Mix of \n and \r\n in the same file
        string text = "FROM alpine\nRUN echo hello\r\n";
        Dockerfile df = Dockerfile.Parse(text);
        Assert.Equal(text, df.ToString());
    }

    [Fact]
    public void Dockerfile_MultipleFromInstructions_ParsesAll()
    {
        string text = "FROM alpine AS base\nFROM base AS final\n";
        Dockerfile df = Dockerfile.Parse(text);
        Assert.Equal(text, df.ToString());
        var froms = df.Items.OfType<FromInstruction>().ToList();
        Assert.Equal(2, froms.Count);
        Assert.Equal("alpine", froms[0].ImageName);
        Assert.Equal("base", froms[0].StageName);
        Assert.Equal("base", froms[1].ImageName);
        Assert.Equal("final", froms[1].StageName);
    }

    [Fact]
    public void Dockerfile_NoTrailingNewline_RoundTrips()
    {
        string text = "FROM alpine";
        Dockerfile df = Dockerfile.Parse(text);
        Assert.Equal(text, df.ToString());
    }

    [Fact]
    public void Dockerfile_BacktickEscape_ParsesCorrectly()
    {
        // When escape directive uses backtick, backtick is the line continuation character
        string text = "# escape=`\nFROM alpine\n";
        Dockerfile df = Dockerfile.Parse(text);
        Assert.Equal(text, df.ToString());
        Assert.Equal('`', df.EscapeChar);
    }

    [Fact]
    public void Dockerfile_ResolveVariables_ArgBeforeFrom_ResolvesInFrom()
    {
        string text = "ARG BASE=alpine\nFROM $BASE\n";
        Dockerfile df = Dockerfile.Parse(text);
        string resolved = df.ResolveVariables();
        Assert.Contains("alpine", resolved);
    }

    [Fact]
    public void Dockerfile_ResolveVariables_EmptyDockerfile_ReturnsEmpty()
    {
        string text = "# just a comment\n";
        Dockerfile df = Dockerfile.Parse(text);
        // No instruction — ResolveVariables should return empty string
        string resolved = df.ResolveVariables();
        Assert.Equal("", resolved);
    }

    [Fact]
    public void Dockerfile_EmptyString_ParsesSuccessfully()
    {
        Dockerfile df = Dockerfile.Parse("");
        Assert.Empty(df.Items);
        Assert.Equal("", df.ToString());
    }

    [Fact]
    public void Dockerfile_OnlyWhitespace_ParsesSuccessfully()
    {
        string text = "\n\n\n";
        Dockerfile df = Dockerfile.Parse(text);
        Assert.Equal(text, df.ToString());
    }

    [Fact]
    public void ResolveArgValues_TargetArgWithOverride_PrecededByArgWithDefault()
    {
        // Regression test for #279: when the target ARG has an override,
        // resolvedValue must still reflect the target instruction, not
        // a preceding ARG that happened to set resolvedValue via the else branch.
        List<string> lines = new()
        {
            "FROM ubuntu",
            "ARG X=hello",
            "ARG Y"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

        Dictionary<string, string?> argValues = new()
        {
            { "Y", "overridden" }
        };

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables(
            (Instruction)dockerfile.Items.Last(), argValues);
        Assert.Equal("ARG Y", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());
    }

    [Fact]
    public void ResolveArgValues_TargetArgWithGlobalArg_PrecededByArgWithDefault()
    {
        // Regression test for #279: when the target ARG matches a global arg,
        // resolvedValue must still reflect the target instruction, not
        // a preceding ARG that happened to set resolvedValue via the else branch.
        List<string> lines = new()
        {
            "ARG G=global",
            "FROM ubuntu",
            "ARG X=hello",
            "ARG G"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables(
            (Instruction)dockerfile.Items.Last());
        Assert.Equal("ARG G", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());
    }

    [Fact]
    public void ResolveArgValues_TargetNonArgInstruction_FollowedByArgWithDefault()
    {
        // Regression test for #279: resolving a non-ARG instruction should
        // return that instruction's resolved text, not a subsequent ARG's text.
        List<string> lines = new()
        {
            "FROM ubuntu",
            "ENV X=hello",
            "ARG Y=default"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables(
            dockerfile.Items.OfType<EnvInstruction>().First());
        Assert.Equal("ENV X=hello\n", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());
    }

    [Fact]
    public void ResolveArgValues_TargetArgWithGlobalArg_RedeclaredInStage()
    {
        List<string> lines = new()
        {
            "ARG BASE=global",
            "FROM ubuntu",
            "ARG BASE",
            "ARG BASE"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables(
            (Instruction)dockerfile.Items.Last());
        Assert.Equal("ARG BASE", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());
    }

    [Fact]
    public void ResolveArgValues_TargetArgWithMultipleDeclarations_UsesLeftToRightScoping()
    {
        List<string> lines = new()
        {
            "FROM ubuntu",
            "ARG A=$B B"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

        Dictionary<string, string?> argValues = new()
        {
            { "B", "override" }
        };

        string originalDockerfileString = dockerfile.ToString();

        string resolvedVal = dockerfile.ResolveVariables(
            (Instruction)dockerfile.Items.Last(), argValues);
        Assert.Equal("ARG A= B", resolvedVal);
        Assert.Equal(originalDockerfileString, dockerfile.ToString());

        dockerfile.ResolveVariables(argValues, options: new ResolutionOptions { UpdateInline = true });

        Assert.Equal("ARG A= B", dockerfile.Items.Last().ToString());
    }
}
