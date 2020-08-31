namespace DockerfileModel
{
    public class Instruction : IDockerfileLine
    {
        internal Instruction(string instructionName, string args)
        {
            this.InstructionName = instructionName;
            this.Args = args;
        }

        public string InstructionName { get; }
        public string Args { get; }
        public LineType Type => LineType.Instruction;
    }
}
