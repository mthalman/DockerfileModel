﻿using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class RunInstructionTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(RunInstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                RunInstruction result = RunInstruction.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => RunInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        //[Theory]
        //[MemberData(nameof(CreateTestInput))]
        //public void Create(CreateTestScenario scenario)
        //{
        //    RunInstruction result = RunInstruction.Create();
        //    Assert.Collection(result.Tokens, scenario.TokenValidators);
        //    scenario.Validate?.Invoke(result);
        //}

        public static IEnumerable<object[]> ParseTestInput()
        {
            RunInstructionParseTestScenario[] testInputs = new RunInstructionParseTestScenario[]
            {
                new RunInstructionParseTestScenario
                {
                    Text = "RUN echo `test",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "RUN"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "echo `test")
                    },
                    Validate = result =>
                    {
                       
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public static IEnumerable<object[]> CreateTestInput()
        {
            CreateTestScenario[] testInputs = new CreateTestScenario[]
            {
                new CreateTestScenario
                {
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class RunInstructionParseTestScenario : ParseTestScenario<RunInstruction>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<RunInstruction>
        {
        }
    }
}
