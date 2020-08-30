namespace DockerfileModel
{
    public class Instruction : IDockerfileLine
    {
        internal Instruction(int lineNumber, string leadingWhitespace, string instructionName, string args)
        {
            this.LineNumber = lineNumber;
            this.LeadingWhitespace = leadingWhitespace;
            this.InstructionName = instructionName;
            this.Args = args;
        }

        public string InstructionName { get; }
        public string Args { get; }

        public int LineNumber { get; }

        public LineType Type => LineType.Instruction;

        public string LeadingWhitespace { get; }
    }
}
