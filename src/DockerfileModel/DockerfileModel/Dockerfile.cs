using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DockerfileModel
{
    public class Dockerfile
    {
        public Dockerfile(IEnumerable<DockerfileLine> dockerfileLines)
        {
            this.Lines = dockerfileLines;
        }

        public IEnumerable<DockerfileLine> Lines { get; }

        public static Dockerfile Parse(string text) =>
            DockerfileParser.ParseContent(text);

        public void ResolveArgValues(IDictionary<string, string?> argValues, char escapeChar)
        {
            StagesView stagesView = new StagesView(this);

            Dictionary<string, string?> globalArgs = stagesView.GlobalArgs
                .ToDictionary(argInstruction => argInstruction.ArgName, argInstruction => argInstruction.ArgValue);

            OverrideArgs(globalArgs, argValues);

            foreach (Stage stage in stagesView.Stages)
            {
                stage.FromInstruction.ResolveArgValues(globalArgs, escapeChar);

                Dictionary<string, string?> stageArgs = new Dictionary<string, string?>();
                foreach (InstructionBase instruction in stage.Lines.OfType<InstructionBase>())
                {
                    if (instruction is ArgInstruction argInstruction)
                    {
                        // If this is just an arg declaration and a value has been provided from a global arg or arg override
                        if (argInstruction.ArgValue is null && globalArgs.TryGetValue(argInstruction.ArgName, out string? globalArg))
                        {
                            stageArgs.Add(argInstruction.ArgName, globalArg);
                        }
                        // If an arg override exists for this arg
                        else if (argValues.TryGetValue(argInstruction.ArgName, out string? overrideArgValue))
                        {
                            stageArgs.Add(argInstruction.ArgName, overrideArgValue);
                        }
                        else
                        {
                            stageArgs[argInstruction.ArgName] = argInstruction.ArgValue;
                        }
                    }
                    else
                    {
                        instruction.ResolveArgValues(stageArgs, escapeChar);
                    }
                }
            }
        }

        private void OverrideArgs(Dictionary<string, string?> declaredArgs, IDictionary<string, string?> overrideArgs)
        {
            foreach (var kvp in overrideArgs)
            {
                if (declaredArgs.ContainsKey(kvp.Key))
                {
                    declaredArgs[kvp.Key] = kvp.Value;
                }
            }
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();

            var lines = Lines.ToArray();
            for (int i = 0; i < lines.Length; i++)
            {
                builder.Append(lines[i].ToString());
            }

            return builder.ToString();
        }
    }
}
