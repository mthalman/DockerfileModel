using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public abstract class Instruction : DockerfileConstruct, ICommentable
{
    private static readonly Dictionary<string, Func<string, char, Instruction>> instructionParsers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "ADD", AddInstruction.Parse },
            { "ARG", ArgInstruction.Parse },
            { "CMD", CmdInstruction.Parse },
            { "COPY", CopyInstruction.Parse },
            { "ENTRYPOINT", EntrypointInstruction.Parse },
            { "EXPOSE", ExposeInstruction.Parse },
            { "ENV", EnvInstruction.Parse },
            { "FROM", FromInstruction.Parse },
            { "HEALTHCHECK", HealthCheckInstruction.Parse },
            { "LABEL", LabelInstruction.Parse },
            { "MAINTAINER", MaintainerInstruction.Parse },
            { "ONBUILD", OnBuildInstruction.Parse },
            { "RUN", RunInstruction.Parse },
            { "SHELL", ShellInstruction.Parse },
            { "STOPSIGNAL", StopSignalInstruction.Parse },
            { "USER", UserInstruction.Parse },
            { "VOLUME", VolumeInstruction.Parse },
            { "WORKDIR", WorkdirInstruction.Parse },
        };

    protected Instruction(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public string InstructionName
    {
        get => this.InstructionNameToken.Value;
    }

    public KeywordToken InstructionNameToken
    {
        get => Tokens.OfType<KeywordToken>().First();
    }

    public IList<string?> Comments => GetComments();

    public IEnumerable<CommentToken> CommentTokens => GetCommentTokens();

    public override ConstructType Type => ConstructType.Instruction;

    internal static Instruction CreateInstruction(string text, char escapeChar)
    {
        string instructionName = InstructionNameParser(escapeChar).Parse(text);
        return instructionParsers[instructionName](text, escapeChar);
    }

    protected static Parser<KeywordToken> InstructionIdentifier(char escapeChar) =>
        instructionParsers.Keys
            .Select(instructionName => KeywordToken.GetParser(instructionName, escapeChar))
            .Aggregate((current, next) => current.Or(next));

    private static Parser<string> InstructionNameParser(char escapeChar) =>
        from leading in Whitespace()
        from instruction in InstructionIdentifier(escapeChar)
        select instruction.Value;
}
