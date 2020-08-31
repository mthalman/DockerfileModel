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
            var parser = DockerfileParser.FromInstruction(testInput.EscapeChar);
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
            void Validate(Instruction instruction, string instructionName, string args)
            {
                Assert.Equal(instructionName, instruction.InstructionName);
                Assert.Equal(args, instruction.Args);
            }

            var testInputs = new TestInput[]
            {
                new InstructionTestInput
                {
                    Content = "FROM base",
                    Validate = result =>
                    {
                        var inst = (Instruction)result;
                        Validate(inst, "FROM", "base");
                    }
                },
                new InstructionTestInput
                {
                    Content = "FROM \\\n  base",
                    Validate = result =>
                    {
                        var inst = (Instruction)result;
                        Validate(inst, "FROM", "base");
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
        }

        public class InstructionArgsTestInput : TestInput
        {
            public char EscapeChar { get; set; } = '\\';
        }
    }
}
