using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class DockerfileTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(ParseTestScenario<Dockerfile> scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                Dockerfile result = Dockerfile.Parse(scenario.Text);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Lines, scenario.TokenValidators);
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
                "FROM image\n",
                "ARG test=a\n",
                "RUN echo $test `$test"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>();
            dockerfile.ResolveArgValues(argValues, '`');

            Assert.Equal("RUN echo a `$test", dockerfile.Lines.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_SingleStage_LocalArg_Override()
        {
            List<string> lines = new List<string>
            {
                "FROM image\n",
                "ARG test=a\n",
                "RUN echo $test `$test"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>
            {
                { "test", "b" }
            };
            dockerfile.ResolveArgValues(argValues, '`');

            Assert.Equal("RUN echo b `$test", dockerfile.Lines.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_MultiStage_LocalArg()
        {
            List<string> lines = new List<string>
            {
                "FROM image as stage1\n",
                "ARG test=a\n",
                "RUN echo $test\n",
                "FROM image2 as stage2\n",
                "ARG test=\n",
                "RUN echo $test-c",
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>();
            dockerfile.ResolveArgValues(argValues, '`');

            StagesView stagesView = new StagesView(dockerfile);
            Assert.Equal("RUN echo a\n", stagesView.Stages.First().Lines.Last().ToString());
            Assert.Equal("RUN echo -c", stagesView.Stages.Last().Lines.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_MultiStage_LocalArg_Override()
        {
            List<string> lines = new List<string>
            {
                "FROM image as stage1\n",
                "ARG test=a\n",
                "RUN echo $test\n",
                "FROM image2 as stage2\n",
                "ARG test=\n",
                "RUN echo $test-c",
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>
            {
                { "test", "z" }
            };
            dockerfile.ResolveArgValues(argValues, '`');

            StagesView stagesView = new StagesView(dockerfile);
            Assert.Equal("RUN echo z\n", stagesView.Stages.First().Lines.Last().ToString());
            Assert.Equal("RUN echo z-c", stagesView.Stages.Last().Lines.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_SingleStage_GlobalArg()
        {
            List<string> lines = new List<string>
            {
                "ARG test1=a\n",
                "ARG test2=b\n",
                "ARG test3=c\n",
                "FROM image:$test1\n",
                "ARG test2\n",
                "ARG test3=c1\n",
                "ARG test4=d\n",
                "RUN echo $test1-$test2-$test3-$test4"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>();
            dockerfile.ResolveArgValues(argValues, '`');

            Assert.Equal("FROM image:a\n", dockerfile.Lines.OfType<FromInstruction>().First().ToString());
            Assert.Equal("RUN echo -b-c1-d", dockerfile.Lines.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_SingleStage_GlobalArg_Override()
        {
            List<string> lines = new List<string>
            {
                "ARG test1=a\n",
                "ARG test2=b\n",
                "ARG test3=c\n",
                "FROM image:$test1\n",
                "ARG test2\n",
                "ARG test3=c1\n",
                "ARG test4=d\n",
                "RUN echo $test1-$test2-$test3-$test4"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>
            {
                { "test1", "a1" },
                { "test2", "b1" },
                { "test3", "c2" },
                { "test4", "d1" }
            };
            dockerfile.ResolveArgValues(argValues, '`');

            Assert.Equal("FROM image:a1\n", dockerfile.Lines.OfType<FromInstruction>().First().ToString());
            Assert.Equal("RUN echo -b1-c2-d1", dockerfile.Lines.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_MultiStage_GlobalArg()
        {
            List<string> lines = new List<string>
            {
                "ARG test1=a\n",
                "ARG test2\n",
                "ARG test3=c\n",
                "FROM image:$test1 as stage1\n",
                "ARG test2\n",
                "ARG test3=c1\n",
                "ARG test4=d\n",
                "RUN echo $test1-$test2-$test3-$test4\n",
                "FROM image:$test2 as stage2\n",
                "ARG test2\n",
                "ARG test3\n",
                "ARG test4=d\n",
                "RUN echo $test1-$test2-$test3-$test4"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>();
            dockerfile.ResolveArgValues(argValues, '`');

            StagesView stagesView = new StagesView(dockerfile);

            Assert.Equal("FROM image:a as stage1\n", stagesView.Stages.First().FromInstruction.ToString());
            Assert.Equal("RUN echo --c1-d\n", stagesView.Stages.First().Lines.Last().ToString());
            Assert.Equal("FROM image: as stage2\n", stagesView.Stages.Last().FromInstruction.ToString());
            Assert.Equal("RUN echo --c-d", stagesView.Stages.Last().Lines.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_MultiStage_GlobalArg_Override()
        {
            List<string> lines = new List<string>
            {
                "ARG test1=a\n",
                "ARG test2\n",
                "ARG test3=c\n",
                "FROM image:$test1 as stage1\n",
                "ARG test2\n",
                "ARG test3=c1\n",
                "ARG test4=d\n",
                "RUN echo $test1-$test2-$test3-$test4\n",
                "FROM image:$test2 as stage2\n",
                "ARG test2\n",
                "ARG test3\n",
                "ARG test4=d\n",
                "RUN echo $test1-$test2-$test3-$test4"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>
            {
                { "test1", "a1" },
                { "test2", "b1" },
                { "test3", "c2" },
            };
            dockerfile.ResolveArgValues(argValues, '`');

            StagesView stagesView = new StagesView(dockerfile);

            Assert.Equal("FROM image:a1 as stage1\n", stagesView.Stages.First().FromInstruction.ToString());
            Assert.Equal("RUN echo -b1-c2-d\n", stagesView.Stages.First().Lines.Last().ToString());
            Assert.Equal("FROM image:b1 as stage2\n", stagesView.Stages.Last().FromInstruction.ToString());
            Assert.Equal("RUN echo -b1-c2-d", stagesView.Stages.Last().Lines.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_ArgOrder()
        {
            List<string> lines = new List<string>
            {
                "FROM image\n",
                "RUN echo $test\n",
                "ARG test=a\n",
                "RUN echo $test"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>();
            dockerfile.ResolveArgValues(argValues, '`');

            Assert.Equal("RUN echo \n", dockerfile.Lines.ElementAt(1).ToString());
            Assert.Equal("RUN echo a", dockerfile.Lines.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_LocalOverride()
        {
            List<string> lines = new List<string>
            {
                "ARG test=a\n",
                "FROM image\n",
                "ARG test\n",
                "RUN echo $test\n",
                "ARG test=b\n",
                "RUN echo $test"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>();
            dockerfile.ResolveArgValues(argValues, '`');

            Assert.Equal("RUN echo a\n", dockerfile.Lines.ElementAt(3).ToString());
            Assert.Equal("RUN echo b", dockerfile.Lines.ElementAt(5).ToString());
        }

        [Fact]
        public void ResolveArgValues_UndeclaredArg()
        {
            List<string> lines = new List<string>
            {
                "FROM image\n",
                "RUN echo $test"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>
            {
                { "test", "foo" }
            };
            dockerfile.ResolveArgValues(argValues, '`');

            Assert.Equal("RUN echo ", dockerfile.Lines.Last().ToString());
        }

        [Fact]
        public void ResolveArgValues_OverrideAsUnset()
        {
            List<string> lines = new List<string>
            {
                "ARG test=foo\n",
                "FROM image\n",
                "RUN echo $test"
            };

            Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

            Dictionary<string, string> argValues = new Dictionary<string, string>
            {
                { "test", null }
            };
            dockerfile.ResolveArgValues(argValues, '`');

            Assert.Equal("RUN echo ", dockerfile.Lines.Last().ToString());
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            var testInputs = new ParseTestScenario<Dockerfile>[]
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
                        line => ValidateAggregate<Instruction>(line, "RUN foo=\"bar\"\n"),
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
                            token => ValidateLiteral(token, "scratch")
                        ),
                    }
                },
                new ParseTestScenario<Dockerfile>
                {
                    Text = $"RUN apt-get update \\\r\n  && apt-get install curl\r\n\r\n#testing",
                    TokenValidators = new Action<Token>[]
                    {
                        line => ValidateAggregate<Instruction>(line, "RUN apt-get update \\\r\n  && apt-get install curl\r\n",
                            new Action<Token>[]
                            {
                                token => ValidateKeyword(token, "RUN"),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLiteral(token, "apt-get update"),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLineContinuation(token, "\\"),
                                token => ValidateNewLine(token, "\r\n"),
                                token => ValidateWhitespace(token, "  "),
                                token => ValidateLiteral(token, "&& apt-get install curl"),
                                token => ValidateNewLine(token, "\r\n"),
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
                        line => ValidateAggregate<Instruction>(line, "RUN apk add \\\n  userspace-rcu",
                            new Action<Token>[]
                            {
                                token => ValidateKeyword(token, "RUN"),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLiteral(token, "apk add"),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLineContinuation(token, "\\"),
                                token => ValidateNewLine(token, "\n"),
                                token => ValidateWhitespace(token, "  "),
                                token => ValidateLiteral(token, "userspace-rcu"),
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
