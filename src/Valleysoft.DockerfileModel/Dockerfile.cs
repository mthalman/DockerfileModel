using System.Text;

namespace Valleysoft.DockerfileModel;

public class Dockerfile : IConstructContainer
{
    public const char DefaultEscapeChar = '\\';

    public Dockerfile() : this(Enumerable.Empty<DockerfileConstruct>())
    {
    }

    public Dockerfile(IEnumerable<DockerfileConstruct> items)
    {
        Requires.NotNull(items, nameof(items));
        this.Items = items.ToList();
    }

    public IList<DockerfileConstruct> Items { get; }

    IEnumerable<DockerfileConstruct> IConstructContainer.Items => Items;

    public char EscapeChar =>
        Items
            .OfType<ParserDirective>()
            .FirstOrDefault(directive => directive.DirectiveName == ParserDirective.EscapeDirective)
            ?.DirectiveValue[0] ?? DefaultEscapeChar; 

    public static Dockerfile Parse(string text)
    {
        Requires.NotNull(text, nameof(text));
        return DockerfileParser.ParseContent(text);
    }

    public string ResolveVariables<TInstruction>(
        TInstruction instruction,
        IDictionary<string, string?>? argValues = null,
        ResolutionOptions? options = null)
        where TInstruction : Instruction
    {
        Requires.NotNull(instruction, nameof(instruction));

        bool foundInstruction = false;

        return ResolveVariables(
            argValues,
            stagesView =>
            {
                Stage? stage = stagesView.Stages
                    .FirstOrDefault(stage => stage.FromInstruction == instruction || stage.Items.Contains(instruction));

                if (stage is null)
                {
                    throw new ArgumentException(
                        $"Instruction '{instruction}' is not contained in this Dockerfile.", nameof(instruction));
                }

                return new Stage[] { stage };
            },
            currentInstruction =>
            {
                if (foundInstruction)
                {
                    return false;
                }

                if (currentInstruction == instruction)
                {
                    foundInstruction = true;
                }

                return currentInstruction is ArgInstruction || foundInstruction;
            },
            options);
    }

    public string ResolveVariables(IDictionary<string, string?>? variableOverrides = null, ResolutionOptions? options = null) =>
        ResolveVariables(
            variableOverrides,
            stagesView => stagesView.Stages,
            instruction => true,
            options);

    public override string ToString()
    {
        StringBuilder builder = new();

        var items = Items.ToArray();
        for (int i = 0; i < items.Length; i++)
        {
            builder.Append(items[i].ToString());
        }

        return builder.ToString();
    }

    private string ResolveVariables(
        IDictionary<string, string?>? variableOverrides,
        Func<StagesView, IEnumerable<Stage>> getStages,
        Func<Instruction, bool> processInstruction,
        ResolutionOptions? options)
    {
        if (variableOverrides is null)
        {
            variableOverrides = new Dictionary<string, string?>();
        }

        if (options is null)
        {
            options = new ResolutionOptions();
        }

        StagesView stagesView = new(this);

        char escapeChar = EscapeChar;
        Dictionary<string, string?> globalArgs = GetGlobalArgs(stagesView, escapeChar, variableOverrides, options);

        var stages = getStages(stagesView);

        string? resolvedValue = null;

        foreach (Stage stage in stages)
        {
            if (processInstruction(stage.FromInstruction))
            {
                resolvedValue = stage.FromInstruction.ResolveVariables(escapeChar, globalArgs, options);
            }

            Dictionary<string, string?> stageArgs = new();

            IEnumerable<Instruction> instructions = stage.Items
                .OfType<Instruction>()
                .Where(instruction => processInstruction(instruction));

            foreach (Instruction instruction in instructions)
            {
                if (instruction is ArgInstruction argInstruction)
                {
                    foreach (ArgDeclaration arg in argInstruction.Args)
                    {
                        // If this is just an arg declaration and a value has been provided from a global arg or arg override
                        if (arg.Value is null && globalArgs.TryGetValue(arg.Name, out string? globalArg))
                        {
                            stageArgs.Add(arg.Name, globalArg);
                        }
                        // If an arg override exists for this arg
                        else if (variableOverrides.TryGetValue(arg.Name, out string? overrideArgValue))
                        {
                            stageArgs.Add(arg.Name, overrideArgValue);
                        }
                        else
                        {
                            string? resolvedArgValue = arg.ValueToken?.ResolveVariables(escapeChar, stageArgs, options);
                            stageArgs[arg.Name] = resolvedArgValue;
                            resolvedValue = instruction.ResolveVariables(escapeChar, stageArgs, options);
                        }
                    }
                }
                else
                {
                    resolvedValue = instruction.ResolveVariables(escapeChar, stageArgs, options);
                }
            }
        }

        return resolvedValue ?? String.Empty;
    }

    private static Dictionary<string, string?> GetGlobalArgs(StagesView stagesView, char escapeChar, IDictionary<string, string?> variables,
        ResolutionOptions options)
    {
        Dictionary<string, string?> globalArgs = new();
        foreach (ArgDeclaration arg in stagesView.GlobalArgs.SelectMany(inst => inst.Args))
        {
            if (variables.TryGetValue(arg.Name, out string? overridenValue))
            {
                globalArgs.Add(arg.Name, overridenValue);
            }
            else
            {
                string? resolvedValue = arg.ValueToken?.ResolveVariables(escapeChar, globalArgs, options);
                globalArgs.Add(arg.Name, resolvedValue);
            }
        }

        return globalArgs;
    }
}
