using System.Collections.Generic;
using System.Linq;
using Validation;

namespace DockerfileModel
{
    public class StagesView
    {
        public StagesView(Dockerfile dockerfile)
        {
            Requires.NotNull(dockerfile, nameof(dockerfile));

            List<DockerfileLine> lines = dockerfile.Lines.ToList();

            List<ArgInstruction> globalArgs = new List<ArgInstruction>();
            List<Stage> stages = new List<Stage>();
            FromInstruction? currentStage = null;
            List<DockerfileLine> stageLines = new List<DockerfileLine>();

            for (int i = 0; i < lines.Count; i++)
            {
                DockerfileLine line = lines[i];
                if (currentStage is null)
                {
                    if (line is ParserDirective || line is Whitespace)
                    {
                        continue;
                    }
                    else if (line is ArgInstruction argInstruction)
                    {
                        globalArgs.Add(argInstruction);
                    }
                    else if (line is FromInstruction fromInstruction)
                    {
                        currentStage = fromInstruction;
                    }
                }
                else
                {
                    if (line is FromInstruction nextFromInstruction)
                    {
                        stages.Add(new Stage(currentStage, stageLines));
                        currentStage = nextFromInstruction;
                        stageLines = new List<DockerfileLine>();
                    }
                    else
                    {
                        stageLines.Add(line);
                    }
                }
            }

            if (currentStage != null)
            {
                stages.Add(new Stage(currentStage, stageLines));
            }

            GlobalArgs = globalArgs;
            Stages = stages;
        }

        public IEnumerable<ArgInstruction> GlobalArgs { get; }
        public IEnumerable<Stage> Stages { get; }
    }

    public class Stage
    {
        public Stage(FromInstruction fromInstruction, IEnumerable<DockerfileLine> lines)
        {
            FromInstruction = fromInstruction;
            Lines = lines;
        }

        public FromInstruction FromInstruction { get; }
        public string? Name => FromInstruction.StageName;
        public IEnumerable<DockerfileLine> Lines { get; }
    }
}
