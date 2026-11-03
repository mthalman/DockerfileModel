using System.Text;
using Valleysoft.DockerfileModel.Tokens;


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
        ArgTokens = new Valleysoft.DockerfileModel.Tokens.TokenList<ArgDeclaration>(this);
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

    internal static ArgInstruction Parse(string text, char escapeChar, Action<string> onDeclarationParsed) =>
        new(GetTokens(text, GetInnerParser(escapeChar, onDeclarationParsed)), escapeChar);

    internal static TextParser<ArgInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
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

    private static TextParser<IEnumerable<Token>> GetInnerParser(char escapeChar,
        Action<string>? onDeclarationParsed = null) =>
        Instruction("ARG", escapeChar, GetArgsParser(escapeChar, onDeclarationParsed));

    private static TextParser<IEnumerable<Token>> GetArgsParser(char escapeChar,
        Action<string>? onDeclarationParsed) =>
        from whitespace in Whitespace().Try().OptionalOrDefault(Enumerable.Empty<Token>())
        from variables in VariablesParser(escapeChar, onDeclarationParsed)
        select ConcatTokens(whitespace, variables);

    internal static TextParser<IEnumerable<Token>> VariablesParser(char escapeChar,
        Action<string>? onDeclarationParsed = null)
    {
        TextParser<ArgDeclaration> declarationParser = ArgDeclaration.GetParser(escapeChar);
        if (onDeclarationParsed is not null)
        {
            TextParser<ArgDeclaration> parser = declarationParser;
            declarationParser = input =>
            {
                Result<ArgDeclaration> result = parser(input);
                if (result.HasValue)
                {
                    onDeclarationParsed(input.Source!.Substring(input.Position.Absolute,
                        result.Remainder.Position.Absolute - input.Position.Absolute));
                }
                return result;
            };
        }
        return ArgTokens(
            from whitespace in Whitespace().Try().OptionalOrDefault(Enumerable.Empty<Token>())
            from variable in declarationParser.AsEnumerable()
            select ConcatTokens(whitespace, variable), escapeChar
        ).Try().AtLeastOnce().Flatten();
    }
}
