using System.Collections.Generic;
using Xunit;

namespace DockerfileModel.Tests
{
    public class CopyInstructionTests : FileTransferInstructionTests<CopyInstruction>
    {
        public CopyInstructionTests()
            : base("COPY", CopyInstruction.Parse,
                  (sources, destination, changeOwner, escapeChar) => new CopyInstruction(sources, destination, changeOwner, escapeChar))
        {
        }

        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(FileTransferInstructionParseTestScenario scenario) => RunParseTest(scenario);

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario) => RunCreateTest(scenario);

        public static IEnumerable<object[]> ParseTestInput() => ParseTestInput("COPY");


        public static IEnumerable<object[]> CreateTestInput() => CreateTestInput("COPY");
    }
}
