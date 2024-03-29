﻿using Xunit;

namespace Valleysoft.DockerfileModel.Tests;

public class AddInstructionTests : FileTransferInstructionTests<AddInstruction>
{
    public AddInstructionTests()
        : base("ADD", AddInstruction.Parse,
            (sources, destination, changeOwner, permissions, escapeChar) =>
                new AddInstruction(sources, destination, changeOwner, permissions, escapeChar))
    {
    }

    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(FileTransferInstructionParseTestScenario scenario) => RunParseTest(scenario);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario) => RunCreateTest(scenario);

    public static IEnumerable<object[]> ParseTestInput() => ParseTestInput("ADD");


    public static IEnumerable<object[]> CreateTestInput() => CreateTestInput("ADD");
}
