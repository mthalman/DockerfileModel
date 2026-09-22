using Valleysoft.DockerfileModel.Tokens;


namespace Valleysoft.DockerfileModel;

/// <summary>A mutable Dockerfile instruction with its original keyword, operands, and formatting tokens.</summary>
/// <remarks>
/// Standalone parsers and constructors default to backslash escaping. Their escape context is retained for
/// later syntax-aware edits; moving an instruction to another document does not change that context.
/// Prefer typed properties and editable collections over low-level token changes for structural edits.
/// </remarks>
public abstract partial class Instruction : DockerfileConstruct, ICommentable
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

    protected Instruction(IEnumerable<Token> tokens) : this(tokens, Dockerfile.DefaultEscapeChar)
    {
    }

    protected Instruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens)
    {
        EditingEscapeChar = escapeChar;
    }

    public string InstructionName
    {
        get => this.InstructionNameToken.Value;
    }

    public KeywordToken InstructionNameToken
    {
        get => Tokens.OfType<KeywordToken>().First();
    }

    /// <summary>Gets a live editable view of comments associated with this instruction.</summary>
    public EditableList<string?> Comments => InstructionCommentEditing.Values(this);

    /// <summary>Gets the corresponding live comment tokens, retaining their identity and formatting.</summary>
    public EditableList<CommentToken> CommentTokens => InstructionCommentEditing.Tokens(this);

    IList<string?> ICommentable.Comments => Comments;

    IEnumerable<CommentToken> ICommentable.CommentTokens => CommentTokens;

    public override ConstructType Type => ConstructType.Instruction;

    internal static Instruction CreateInstruction(string text, char escapeChar)
    {
        string instructionName = InstructionNameParser(escapeChar).Parse(text);
        return instructionParsers[instructionName](text, escapeChar);
    }

    internal static bool IsKnownInstruction(string name) => instructionParsers.ContainsKey(name);

    internal static Instruction CreateDiagnosticInstruction(
        string name, string text, char escapeChar, InstructionParseContext context) =>
        name.ToUpperInvariant() switch
        {
            "RUN" => RunInstruction.ParseDiagnostic(text, escapeChar, context),
            "CMD" => CmdInstruction.ParseDiagnostic(text, escapeChar),
            "ENTRYPOINT" => EntrypointInstruction.ParseDiagnostic(text, escapeChar),
            "HEALTHCHECK" => HealthCheckInstruction.ParseDiagnostic(text, escapeChar),
            "COPY" => CopyInstruction.ParseDiagnostic(text, escapeChar, context),
            "ADD" => AddInstruction.ParseDiagnostic(text, escapeChar, context),
            "ONBUILD" => OnBuildInstruction.ParseDiagnostic(text, escapeChar, context),
            _ => instructionParsers[name](text, escapeChar)
        };

    protected static Parser<KeywordToken> InstructionIdentifier(char escapeChar) =>
        instructionParsers.Keys
            .Select(instructionName => KeywordToken.GetParser(instructionName, escapeChar))
            .Aggregate((current, next) => current.Or(next));

    internal static Parser<string> InstructionNameParser(char escapeChar) =>
        from leading in Whitespace()
        from instruction in InstructionIdentifier(escapeChar)
        select instruction.Value;
}
