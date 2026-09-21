using System.Text;
using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

/// <summary>An ordered set of ARG declarations with optional defaults.</summary>
/// <remarks>Declaration order matters when later defaults reference earlier declarations.</remarks>
public class ArgInstruction : Instruction
{
    /// <summary>Creates an ARG declaration; null omits the default while an empty string emits an empty assignment.</summary>
    public ArgInstruction(string argName, string? argValue = null,
        char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(
            new Dictionary<string, string?>
            {
                { argName, argValue }
            },
            escapeChar)
    {
    }

    /// <summary>Creates ARG declarations in dictionary enumeration order, retaining the supplied escape context.</summary>
    public ArgInstruction(IDictionary<string, string?> args, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(args, escapeChar), escapeChar)
    {
    }

    private ArgInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
        ArgTokens = new TokenList<ArgDeclaration>(this);
        Args = InstructionCollectionEditing.Pairs(ArgTokens, this);
    }

    /// <summary>Gets the live syntax-aware declaration view; its pair objects remain connected to the instruction.</summary>
    /// <remarks>Null values mean no default, distinct from empty defaults. At least one declaration must remain.</remarks>
    public EditableList<IKeyValuePair> Args { get; }

    /// <summary>Gets the live declaration token view for edits that need explicit syntax or token identity.</summary>
    public EditableList<ArgDeclaration> ArgTokens { get; }

    /// <summary>Parses a standalone ARG instruction, preserving declaration order, formatting, and escape context.</summary>
    public static ArgInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<ArgInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new ArgInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(IDictionary<string, string?> args, char escapeChar)
    {
        Guard.NotNullOrEmpty(args, nameof(args));

        string[] keyValueAssignments = args
            .Select(kvp => kvp.Value is not null
                ? StringHelper.FormatKeyValueAssignment(kvp.Key, kvp.Value)
                : kvp.Key)
            .ToArray();

        return GetTokens($"ARG {string.Join(" ", keyValueAssignments)}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("ARG", escapeChar, GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        from whitespace in Whitespace().Optional()
        from variables in VariablesParser(escapeChar)
        select ConcatTokens(whitespace.GetOrDefault(), variables);

    internal static Parser<IEnumerable<Token>> VariablesParser(char escapeChar) =>
        ArgTokens(
            from whitespace in Whitespace().Optional()
            from variable in ArgDeclaration.GetParser(escapeChar).AsEnumerable()
            select ConcatTokens(whitespace.GetOrDefault(), variable), escapeChar
        ).AtLeastOnce().Flatten();
}
