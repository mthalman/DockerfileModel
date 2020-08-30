using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;
using Xunit;

namespace DockerfileModel.Tests
{
    public class DockerfileParserTests
    {
        [Theory]
        [MemberData(nameof(InstructionArgsInput))]
        public void InstructionArgs(InstructionArgsTestInput testInput)
        {
            var parser = DockerfileParser.InstructionArgs(testInput.EscapeChar);
            var result = parser.Parse(testInput.Content);
            testInput.Validate(result);
        }

        [Theory]
        [MemberData(nameof(FromInstructionInput))]
        public void FromInstruction(InstructionTestInput testInput)
        {
            var parser = DockerfileParser.FromInstruction(testInput.LineNumber, testInput.EscapeChar);
            var result = parser.Parse(testInput.Content);
            testInput.Validate(result);
        }

        public static IEnumerable<object[]> InstructionArgsInput()
        {
            var testInputs = new TestInput[]
            {
                new InstructionArgsTestInput
                {
                    Content = "abc 1 2 3",
                    Validate = result =>
                    {
                        var args = (string)result;
                        Assert.Equal("abc 1 2 3", args);
                    }
                },
                new InstructionArgsTestInput
                {
                    Content = "",
                    Validate = result =>
                    {
                        var args = (string)result;
                        Assert.Equal("", args);
                    }
                },
                new InstructionArgsTestInput
                {
                    Content = "\n",
                    Validate = result =>
                    {
                        var args = (string)result;
                        Assert.Equal("", args);
                    }
                },
                new InstructionArgsTestInput
                {
                    Content = "  \n",
                    Validate = result =>
                    {
                        var args = (string)result;
                        Assert.Equal("", args);
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public static IEnumerable<object[]> FromInstructionInput()
        {
            void Validate(Instruction instruction, int lineNumber, string instructionName, string args, string leadingWhitespace)
            {
                Assert.Equal(lineNumber, instruction.LineNumber);
                Assert.Equal(instructionName, instruction.InstructionName);
                Assert.Equal(args, instruction.Args);
                Assert.Equal(leadingWhitespace, instruction.LeadingWhitespace);
            }

            var testInputs = new TestInput[]
            {
                new InstructionTestInput
                {
                    Content = "FROM base",
                    LineNumber = 2,
                    Validate = result =>
                    {
                        var inst = (Instruction)result;
                        Validate(inst, 2, "FROM", "base", "");
                    }
                },
                new InstructionTestInput
                {
                    Content = "FROM \\\n  base",
                    LineNumber = 1,
                    Validate = result =>
                    {
                        var inst = (Instruction)result;
                        Validate(inst, 1, "FROM", "base", "");
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class TestInput
        {
            public string Content { get; set; }
            public Action<object> Validate { get; set; }
        }

        public class InstructionTestInput : TestInput
        {
            public char EscapeChar { get; set; } = '\\';
            public int LineNumber { get; set; } = 1;
        }

        public class InstructionArgsTestInput : TestInput
        {
            public char EscapeChar { get; set; } = '\\';
        }
    }
}
