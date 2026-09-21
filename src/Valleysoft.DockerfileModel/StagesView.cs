namespace Valleysoft.DockerfileModel;

/// <summary>Groups a document's global ARGs and FROM-delimited build stages.</summary>
/// <remarks>
/// Membership is captured at construction, but the constructs remain shared with the document.
/// Recreate this view after structural edits. For semantic dependencies and image references, use <c>Dockerfile.Analyze()</c>.
/// </remarks>
public class StagesView
{
    /// <summary>Captures the current grouping without cloning or modifying the document.</summary>
    /// <param name="dockerfile">The document whose constructs are grouped.</param>
    public StagesView(Dockerfile dockerfile)
    {
        Guard.NotNull(dockerfile, nameof(dockerfile));

        List<DockerfileConstruct> items = dockerfile.Items.ToList();

        List<ArgInstruction> globalArgs = new();
        List<Stage> stages = new();
        FromInstruction? currentStage = null;
        List<DockerfileConstruct> stageItems = new();

        for (int i = 0; i < items.Count; i++)
        {
            DockerfileConstruct item = items[i];
            if (currentStage is null)
            {
                if (item is ParserDirective || item is Whitespace)
                {
                    continue;
                }
                else if (item is ArgInstruction argInstruction)
                {
                    globalArgs.Add(argInstruction);
                }
                else if (item is FromInstruction fromInstruction)
                {
                    currentStage = fromInstruction;
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

        GlobalArgs = globalArgs;
        Stages = stages;
    }

    /// <summary>Gets ARG instructions preceding the first FROM, in source order.</summary>
    public IEnumerable<ArgInstruction> GlobalArgs { get; }
    /// <summary>Gets captured stages in source order; documents without FROM have no stages.</summary>
    public IEnumerable<Stage> Stages { get; }
}

/// <summary>A captured stage grouping whose instruction and item objects are shared with the document.</summary>
public class Stage : IConstructContainer
{
    internal Stage(FromInstruction fromInstruction, IEnumerable<DockerfileConstruct> items)
    {
        FromInstruction = fromInstruction;
        Items = items;
    }

    /// <summary>Gets the stage's opening FROM instruction, which is not part of <see cref="Items"/>.</summary>
    public FromInstruction FromInstruction { get; }
    /// <summary>Gets the current FROM alias, or null for an unnamed stage.</summary>
    public string? Name => FromInstruction.StageName;
    /// <summary>Gets captured constructs after the opening FROM and before the next FROM, including comments and whitespace.</summary>
    public IEnumerable<DockerfileConstruct> Items { get; }
}
