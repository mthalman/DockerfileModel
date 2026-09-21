using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.BasicParsers;
using static Valleysoft.DockerfileModel.Parsing.CommandParsers;
using static Valleysoft.DockerfileModel.Parsing.InstructionParsers;
using static Valleysoft.DockerfileModel.Parsing.TokenSequences;
using static Valleysoft.DockerfileModel.Parsing.VariableParsers;

namespace Valleysoft.DockerfileModel;

public class VolumeInstruction : Instruction
{
    public VolumeInstruction(string path, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(new string[] { path }, escapeChar)
    {
    }

    public VolumeInstruction(IEnumerable<string> paths, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(paths, escapeChar), escapeChar)
    {
    }

    private VolumeInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
        PathTokens = new TokenList<LiteralToken>(this);
        Paths = InstructionCollectionEditing.Strings(PathTokens, this);
    }

    /// <summary>Gets the live editable volume path value view.</summary>
    /// <remarks>
    /// New strings supply semantic values. In JSON form, values containing double quotes,
    /// backslashes, or U+0000–U+001F are rejected because this view does not implement JSON escaping.
    /// Use <see cref="PathTokens"/> for valid encoded syntax within the existing operand grammar.
    /// </remarks>
    public EditableList<string> Paths { get; }

    /// <summary>Gets the live editable volume path token view.</summary>
    /// <remarks>
    /// In JSON form, inserted and non-self replacement tokens must form one JSON string when
    /// double-quoted and normalized for Dockerfile continuations and physical comments.
    /// Valid encoded syntax is preserved; rejection leaves incoming tokens unchanged.
    /// Existing operand grammar checks still apply.
    /// </remarks>
    public EditableList<LiteralToken> PathTokens { get; }

    public static VolumeInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<VolumeInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new VolumeInstruction(tokens, escapeChar);

    internal static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("VOLUME", escapeChar, GetArgsParser(escapeChar));

    private static IEnumerable<Token> GetTokens(IEnumerable<string> paths, char escapeChar)
    {
        Guard.NotNullEmptyOrNullElements(paths, nameof(paths));
        string[] pathArray = paths.ToArray();
        bool useShellForm = pathArray.Length == 1 && pathArray[0].Length > 0 && !pathArray[0].Any(char.IsWhiteSpace);
        string args = useShellForm
            ? pathArray[0]
            : StringHelper.FormatAsJson(pathArray);
        return GetTokens($"VOLUME {args}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        from whitespace in Whitespace()
        from paths in ArgTokens(GetPathsParser(escapeChar), escapeChar)
        select ConcatTokens(
            whitespace, paths);

    private static Parser<IEnumerable<Token>> GetPathsParser(char escapeChar) =>
        JsonArray(escapeChar, canContainVariables: false, allowEmpty: true)
            .XOr(NonJsonPaths(escapeChar));

    private static Parser<IEnumerable<Token>> NonJsonPaths(char escapeChar) =>
        ArgTokens(
            from whitespace in Whitespace().Optional()
            from path in LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.AllowedInQuotes).AsEnumerable()
            select ConcatTokens(whitespace.GetOrDefault(), path), escapeChar
        ).AtLeastOnce().Flatten();
}
