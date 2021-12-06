using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class VolumeInstruction : Instruction
{
    public VolumeInstruction(string path, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(new string[] { path }, escapeChar)
    {
    }

    public VolumeInstruction(IEnumerable<string> paths, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(paths, escapeChar))
    {
    }

    private VolumeInstruction(IEnumerable<Token> tokens) : base(tokens)
    {
        PathTokens = new TokenList<LiteralToken>(TokenList);
        Paths = new ProjectedItemList<LiteralToken, string>(
            PathTokens,
            token => token.Value,
            (token, value) => token.Value = value);
    }

    public IList<string> Paths { get; }

    public IList<LiteralToken> PathTokens { get; }

    public static VolumeInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<VolumeInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new VolumeInstruction(tokens);

    internal static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("VOLUME", escapeChar, GetArgsParser(escapeChar));

    private static IEnumerable<Token> GetTokens(IEnumerable<string> paths, char escapeChar)
    {
        Requires.NotNullEmptyOrNullElements(paths, nameof(paths));
        return GetTokens($"VOLUME {StringHelper.FormatAsJson(paths)}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        from mounts in ArgTokens(MountFlag.GetParser(escapeChar).AsEnumerable(), escapeChar).Many()
        from whitespace in Whitespace()
        from command in ArgTokens(GetPathsParser(escapeChar), escapeChar)
        select ConcatTokens(
            mounts.Flatten(), whitespace, command);

    private static Parser<IEnumerable<Token>> GetPathsParser(char escapeChar) =>
        JsonArray(escapeChar, canContainVariables: false)
            .XOr(NonJsonPaths(escapeChar));

    private static Parser<IEnumerable<Token>> NonJsonPaths(char escapeChar) =>
        ArgTokens(
            from whitespace in Whitespace().Optional()
            from path in LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.AllowedInQuotes).AsEnumerable()
            select ConcatTokens(whitespace.GetOrDefault(), path), escapeChar
        ).AtLeastOnce().Flatten();
}
