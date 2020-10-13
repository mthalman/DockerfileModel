using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
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
            List<string> lines = new List<string>
            {
                "# escape=`",
                "FROM image",
                "ARG test=a",
                "RUN echo $test `$test"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

            string originalDockerfileString = dockerfile.ToString();

            string resolvedVal = dockerfile.ResolveVariables((RunInstruction)dockerfile.Items.Last());
            Assert.Equal("RUN echo a `$test", resolvedVal);
            Assert.Equal(originalDockerfileString, dockerfile.ToString());

            dockerfile.ResolveVariables();

            Assert.Equal("RUN echo a `$test", dockerfile.Items.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_SingleStage_LocalArg_Override()
        {
            List<string> lines = new List<string>
            {
                "FROM image",
                "ARG test=a",
                "RUN echo $test \\$test"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>
            {
                { "test", "b" }
            };

            string originalDockerfileString = dockerfile.ToString();

            string resolvedVal = dockerfile.ResolveVariables((RunInstruction)dockerfile.Items.Last(), argValues);
            Assert.Equal("RUN echo b \\$test", resolvedVal);
            Assert.Equal(originalDockerfileString, dockerfile.ToString());

            dockerfile.ResolveVariables(argValues);

            Assert.Equal("RUN echo b \\$test", dockerfile.Items.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_MultiStage_LocalArg()
        {
            List<string> lines = new List<string>
            {
                "FROM image as stage1",
                "ARG test=a",
                "RUN echo $test",
                "FROM image2 as stage2",
                "ARG test=",
                "RUN echo $test-c",
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));
            StagesView stagesView = new StagesView(dockerfile);

            string originalDockerfileString = dockerfile.ToString();

            string resolvedVal = dockerfile.ResolveVariables((RunInstruction)stagesView.Stages.First().Items.Last());
            Assert.Equal("RUN echo a\n", resolvedVal);
            resolvedVal = dockerfile.ResolveVariables((RunInstruction)stagesView.Stages.Last().Items.Last());
            Assert.Equal("RUN echo -c", resolvedVal);
            Assert.Equal(originalDockerfileString, dockerfile.ToString());

            dockerfile.ResolveVariables();

            Assert.Equal("RUN echo a\n", stagesView.Stages.First().Items.Last().ToString());
            Assert.Equal("RUN echo -c", stagesView.Stages.Last().Items.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_MultiStage_LocalArg_Override()
        {
            List<string> lines = new List<string>
            {
                "FROM image as stage1",
                "ARG test=a",
                "RUN echo $test",
                "FROM image2 as stage2",
                "ARG test=",
                "RUN echo $test-c",
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));
            StagesView stagesView = new StagesView(dockerfile);

            Dictionary<string, string> argValues = new Dictionary<string, string>
            {
                { "test", "z" }
            };

            string originalDockerfileString = dockerfile.ToString();

            string resolvedVal = dockerfile.ResolveVariables(
                (RunInstruction)stagesView.Stages.First().Items.Last(), argValues);
            Assert.Equal("RUN echo z\n", resolvedVal);
            resolvedVal = dockerfile.ResolveVariables(
                (RunInstruction)stagesView.Stages.Last().Items.Last(), argValues);
            Assert.Equal("RUN echo z-c", resolvedVal);
            Assert.Equal(originalDockerfileString, dockerfile.ToString());

            dockerfile.ResolveVariables(argValues);
            
            Assert.Equal("RUN echo z\n", stagesView.Stages.First().Items.Last().ToString());
            Assert.Equal("RUN echo z-c", stagesView.Stages.Last().Items.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_SingleStage_GlobalArg()
        {
            List<string> lines = new List<string>
            {
                "ARG test1=a",
                "ARG test2=b",
                "ARG test3=c",
                "FROM image:$test1",
                "ARG test2",
                "ARG test3=c1",
                "ARG test4=d",
                "RUN echo $test1-$test2-$test3-$test4"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

            string originalDockerfileString = dockerfile.ToString();

            string resolvedVal = dockerfile.ResolveVariables(
                dockerfile.Items.OfType<FromInstruction>().First());
            Assert.Equal("FROM image:a\n", resolvedVal);
            resolvedVal = dockerfile.ResolveVariables(
                (RunInstruction)dockerfile.Items.Last());
            Assert.Equal("RUN echo -b-c1-d", resolvedVal);
            Assert.Equal(originalDockerfileString, dockerfile.ToString());

            dockerfile.ResolveVariables();

            Assert.Equal("FROM image:a\n", dockerfile.Items.OfType<FromInstruction>().First().ToString());
            Assert.Equal("RUN echo -b-c1-d", dockerfile.Items.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_SingleStage_GlobalArg_Override()
        {
            List<string> lines = new List<string>
            {
                "ARG test1=a",
                "ARG test2=b",
                "ARG test3=c",
                "FROM image:$test1",
                "ARG test2",
                "ARG test3=c1",
                "ARG test4=d",
                "RUN echo $test1-$test2-$test3-$test4"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>
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
                (RunInstruction)dockerfile.Items.Last(), argValues);
            Assert.Equal("RUN echo -b1-c2-d1", resolvedVal);
            Assert.Equal(originalDockerfileString, dockerfile.ToString());

            dockerfile.ResolveVariables(argValues);

            Assert.Equal("FROM image:a1\n", dockerfile.Items.OfType<FromInstruction>().First().ToString());
            Assert.Equal("RUN echo -b1-c2-d1", dockerfile.Items.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_MultiStage_GlobalArg()
        {
            List<string> lines = new List<string>
            {
                "ARG test1=a",
                "ARG test2",
                "ARG test3=c",
                "FROM image:$test1 as stage1",
                "ARG test2",
                "ARG test3=c1",
                "ARG test4=d",
                "RUN echo $test1-$test2-$test3-$test4",
                "FROM image:$test2 as stage2",
                "ARG test2",
                "ARG test3",
                "ARG test4=d",
                "RUN echo $test1-$test2-$test3-$test4"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));
            StagesView stagesView = new StagesView(dockerfile);

            string originalDockerfileString = dockerfile.ToString();

            string resolvedVal = dockerfile.ResolveVariables(
                stagesView.Stages.First().FromInstruction);
            Assert.Equal("FROM image:a as stage1\n", resolvedVal);
            resolvedVal = dockerfile.ResolveVariables(
                (RunInstruction)stagesView.Stages.First().Items.Last());
            Assert.Equal("RUN echo --c1-d\n", resolvedVal);
            resolvedVal = dockerfile.ResolveVariables(
                stagesView.Stages.Last().FromInstruction);
            Assert.Equal("FROM image: as stage2\n", resolvedVal);
            resolvedVal = dockerfile.ResolveVariables(
                (RunInstruction)stagesView.Stages.Last().Items.Last());
            Assert.Equal("RUN echo --c-d", resolvedVal);
            Assert.Equal(originalDockerfileString, dockerfile.ToString());

            dockerfile.ResolveVariables();

            Assert.Equal("FROM image:a as stage1\n", stagesView.Stages.First().FromInstruction.ToString());
            Assert.Equal("RUN echo --c1-d\n", stagesView.Stages.First().Items.Last().ToString());
            Assert.Equal("FROM image: as stage2\n", stagesView.Stages.Last().FromInstruction.ToString());
            Assert.Equal("RUN echo --c-d", stagesView.Stages.Last().Items.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_MultiStage_GlobalArg_Override()
        {
            List<string> lines = new List<string>
            {
                "ARG test1=a",
                "ARG test2",
                "ARG test3=c",
                "FROM image:$test1 as stage1",
                "ARG test2",
                "ARG test3=c1",
                "ARG test4=d",
                "RUN echo $test1-$test2-$test3-$test4",
                "FROM image:$test2 as stage2",
                "ARG test2",
                "ARG test3",
                "ARG test4=d",
                "RUN echo $test1-$test2-$test3-$test4"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));
            StagesView stagesView = new StagesView(dockerfile);

            Dictionary<string, string> argValues = new Dictionary<string, string>
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
                (RunInstruction)stagesView.Stages.First().Items.Last(), argValues);
            Assert.Equal("RUN echo -b1-c2-d\n", resolvedVal);
            resolvedVal = dockerfile.ResolveVariables(
                stagesView.Stages.Last().FromInstruction, argValues);
            Assert.Equal("FROM image:b1 as stage2\n", resolvedVal);
            resolvedVal = dockerfile.ResolveVariables(
                (RunInstruction)stagesView.Stages.Last().Items.Last(), argValues);
            Assert.Equal("RUN echo -b1-c2-d", resolvedVal);
            Assert.Equal(originalDockerfileString, dockerfile.ToString());

            dockerfile.ResolveVariables(argValues);

            Assert.Equal("FROM image:a1 as stage1\n", stagesView.Stages.First().FromInstruction.ToString());
            Assert.Equal("RUN echo -b1-c2-d\n", stagesView.Stages.First().Items.Last().ToString());
            Assert.Equal("FROM image:b1 as stage2\n", stagesView.Stages.Last().FromInstruction.ToString());
            Assert.Equal("RUN echo -b1-c2-d", stagesView.Stages.Last().Items.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_ArgOrder()
        {
            List<string> lines = new List<string>
            {
                "FROM image",
                "RUN echo $test",
                "ARG test=a",
                "RUN echo $test"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>();

            string originalDockerfileString = dockerfile.ToString();

            string resolvedVal = dockerfile.ResolveVariables(
                (RunInstruction)dockerfile.Items.ElementAt(1), argValues);
            Assert.Equal("RUN echo \n", resolvedVal);
            resolvedVal = dockerfile.ResolveVariables(
                (RunInstruction)dockerfile.Items.Last(), argValues);
            Assert.Equal("RUN echo a", resolvedVal);
            Assert.Equal(originalDockerfileString, dockerfile.ToString());

            dockerfile.ResolveVariables(argValues);

            Assert.Equal("RUN echo \n", dockerfile.Items.ElementAt(1).ToString());
            Assert.Equal("RUN echo a", dockerfile.Items.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_LocalOverride()
        {
            List<string> lines = new List<string>
            {
                "ARG test=a",
                "FROM image",
                "ARG test",
                "RUN echo $test",
                "ARG test=b",
                "RUN echo $test"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

            string originalDockerfileString = dockerfile.ToString();

            string resolvedVal = dockerfile.ResolveVariables(
                (RunInstruction)dockerfile.Items.ElementAt(3));
            Assert.Equal("RUN echo a\n", resolvedVal);
            resolvedVal = dockerfile.ResolveVariables(
                (RunInstruction)dockerfile.Items.ElementAt(5));
            Assert.Equal("RUN echo b", resolvedVal);
            Assert.Equal(originalDockerfileString, dockerfile.ToString());

            dockerfile.ResolveVariables();

            Assert.Equal("RUN echo a\n", dockerfile.Items.ElementAt(3).ToString());
            Assert.Equal("RUN echo b", dockerfile.Items.ElementAt(5).ToString());
        }

        [Fact]
        public void ResolveArgValues_DependentArgs()
        {
            List<string> lines = new List<string>
            {
                "ARG test1=a",
                "ARG test2=$test1",
                "ARG test3=${test2}",
                "FROM image:$test3"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));
            dockerfile.ResolveVariables();

            Assert.Equal("ARG test2=a\n", dockerfile.Items.ElementAt(1).ToString());
            Assert.Equal("ARG test3=a\n", dockerfile.Items.ElementAt(2).ToString());
            Assert.Equal("FROM image:a", dockerfile.Items.ElementAt(3).ToString());
        }

        [Fact]
        public void ResolveArgValues_DependentArgs_Overriden()
        {
            List<string> lines = new List<string>
            {
                "ARG test1",
                "ARG test2=$test1",
                "ARG test3=${test1+bar}",
                "FROM image:$test2",
                "FROM image:$test3"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>
            {
                { "test1", "foo" }
            };
            dockerfile.ResolveVariables(argValues);

            Assert.Equal("ARG test2=foo\n", dockerfile.Items.ElementAt(1).ToString());
            Assert.Equal("ARG test3=bar\n", dockerfile.Items.ElementAt(2).ToString());
            Assert.Equal("FROM image:foo", dockerfile.Items.ElementAt(3).ToString());
            Assert.Equal("FROM image:bar", dockerfile.Items.ElementAt(4).ToString());
        }

        [Fact]
        public void ResolveArgValues_UndeclaredArg()
        {
            List<string> lines = new List<string>
            {
                "FROM image",
                "RUN echo $test"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>
            {
                { "test", "foo" }
            };

            string originalDockerfileString = dockerfile.ToString();

            string resolvedVal = dockerfile.ResolveVariables(
                (RunInstruction)dockerfile.Items.Last(), argValues);
            Assert.Equal("RUN echo ", resolvedVal);
            Assert.Equal(originalDockerfileString, dockerfile.ToString());

            dockerfile.ResolveVariables(argValues);

            Assert.Equal("RUN echo ", dockerfile.Items.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_OverrideAsUnset()
        {
            List<string> lines = new List<string>
            {
                "ARG test=foo",
                "FROM image",
                "RUN echo $test"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("\n", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>
            {
                { "test", null }
            };

            string originalDockerfileString = dockerfile.ToString();

            string resolvedVal = dockerfile.ResolveVariables(
                (RunInstruction)dockerfile.Items.Last(), argValues);
            Assert.Equal("RUN echo ", resolvedVal);
            Assert.Equal(originalDockerfileString, dockerfile.ToString());

            dockerfile.ResolveVariables(argValues);

            Assert.Equal("RUN echo ", dockerfile.Items.Last().ToString());
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            ParseTestScenario<Dockerfile>[] testInputs = new ParseTestScenario<Dockerfile>[]
            {
                new ParseTestScenario<Dockerfile>
                {
                    Text = "FROM scratch",
                    TokenValidators = new Action<Token>[]
                    {
                        line => ValidateAggregate<FromInstruction>(line, "FROM scratch")
                    }
                },
                new ParseTestScenario<Dockerfile>
                {
                    Text = "FROM \\\r\nscratch",
                    TokenValidators = new Action<Token>[]
                    {
                        line => ValidateAggregate<FromInstruction>(line, $"FROM \\\r\nscratch")
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
                            token => ValidateLineContinuation(token, $"`"),
                            token => ValidateNewLine(token, "\r\n"),
                            token => ValidateWhitespace(token, "  "),
                            token => ValidateQuotableAggregate<ImageName>(token, "scratch", null,
                                token => ValidateLiteral(token, "scratch")))
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
                            token => ValidateLineContinuation(token, "\\"),
                            token => ValidateNewLine(token, "\n"),
                            token => ValidateAggregate<CommentToken>(token, "#comment",
                                token => ValidateSymbol(token, "#"),
                                token => ValidateLiteral(token, "comment")),
                            token => ValidateNewLine(token, "\n"),
                            token => ValidateQuotableAggregate<ImageName>(token, "scratch", null,
                                token => ValidateLiteral(token, "scratch"))
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
                                token => ValidateAggregate<RunArgs>(token, "apt-get update \\\r\n  && apt-get install curl\r\n",
                                    token => ValidateLiteral(token, "apt-get update"),
                                    token => ValidateWhitespace(token, " "),
                                    token => ValidateLineContinuation(token, "\\"),
                                    token => ValidateNewLine(token, "\r\n"),
                                    token => ValidateWhitespace(token, "  "),
                                    token => ValidateLiteral(token, "&& apt-get install curl"),
                                    token => ValidateNewLine(token, "\r\n")),
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
                                token => ValidateAggregate<RunArgs>(token, "apk add \\\n  userspace-rcu",
                                    token => ValidateLiteral(token, "apk add"),
                                    token => ValidateWhitespace(token, " "),
                                    token => ValidateLineContinuation(token, "\\"),
                                    token => ValidateNewLine(token, "\n"),
                                    token => ValidateWhitespace(token, "  "),
                                    token => ValidateLiteral(token, "userspace-rcu")),
                            })
                    }
                },
                new ParseTestScenario<Dockerfile>
                {
                    Text = $"ENV \\\t \n VAR=VAL",
                    TokenValidators = new Action<Token>[]
                    {
                        line => ValidateAggregate<Instruction>(line, "ENV \\\t \n VAR=VAL",
                            new Action<Token>[]
                            {
                                token => ValidateKeyword(token, "ENV"),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLineContinuation(token, "\\"),
                                token => ValidateWhitespace(token, "\t "),
                                token => ValidateNewLine(token, "\n"),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLiteral(token, "VAR=VAL"),
                            })
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }
    }
}
