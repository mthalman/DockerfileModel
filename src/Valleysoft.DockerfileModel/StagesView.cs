namespace Valleysoft.DockerfileModel;

public class StagesView
{
    public StagesView(Dockerfile dockerfile)
    {
        Requires.NotNull(dockerfile, nameof(dockerfile));

        List<DockerfileConstruct> items = dockerfile.Items.ToList();

        List<DockerfileConstruct> globalItems = new();
        List<Stage> stages = new();
        FromInstruction? currentStage = null;
        List<DockerfileConstruct> stageItems = new();

        for (int i = 0; i < items.Count; i++)
        {
            DockerfileConstruct item = items[i];
            if (currentStage is null)
            {
                if (item is ParserDirective)
                {
                    continue;
                }
                else if (item is FromInstruction fromInstruction)
                {
                    currentStage = fromInstruction;
                }
                else
                {
                    globalItems.Add(item);
                }
            }
            else
            {
                if (item is FromInstruction nextFromInstruction)
                {
                    stages.Add(new Stage(currentStage, stageItems));
                    currentStage = nextFromInstruction;
                    stageItems = new List<DockerfileConstruct>();
                }
                else
                {
                    stageItems.Add(item);
                }
            }
        }

        if (currentStage != null)
        {
            stages.Add(new Stage(currentStage, stageItems));
        }

        GlobalItems = globalItems;
        Stages = stages;
    }

    public IEnumerable<ArgInstruction> GlobalArgs => GlobalItems.OfType<ArgInstruction>();
    public IEnumerable<DockerfileConstruct> GlobalItems { get; }
    public IEnumerable<Stage> Stages { get; }
}

public class Stage : IConstructContainer
{
    internal Stage(FromInstruction fromInstruction, IEnumerable<DockerfileConstruct> items)
    {
        FromInstruction = fromInstruction;
        Items = items;
    }

    public FromInstruction FromInstruction { get; }
    public string? Name => FromInstruction.StageName;
    public IEnumerable<DockerfileConstruct> Items { get; }
}
