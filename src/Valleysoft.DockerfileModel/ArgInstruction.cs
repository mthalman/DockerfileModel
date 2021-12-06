using System.Text;
using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class ArgInstruction : Instruction
{
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

    public ArgInstruction(IDictionary<string, string?> args, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(args, escapeChar))
    {
    }

    private ArgInstruction(IEnumerable<Token> tokens) : base(tokens)
    {
        ArgTokens = new TokenList<ArgDeclaration>(TokenList);
        Args = new ProjectedItemList<ArgDeclaration, IKeyValuePair>(
            ArgTokens,
            token => token,
            (token, keyValuePair) =>
            {
                Requires.NotNull(keyValuePair, "value");
                token.Name = keyValuePair.Key;
                token.Value = keyValuePair.Value;
            });
    }

    public IList<IKeyValuePair> Args { get; }

    public IList<ArgDeclaration> ArgTokens { get; }

    public static ArgInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<ArgInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new ArgInstruction(tokens);

    private static IEnumerable<Token> GetTokens(IDictionary<string, string?> args, char escapeChar)
    {
        Requires.NotNullOrEmpty(args, nameof(args));

        string[] keyValueAssignments = args
            .Select(kvp =>
            {
                StringBuilder builder = new(kvp.Key);

                string? value = kvp.Value;
                if (value is not null)
                {
                    builder.Append('=');

                    bool requiresQuotes =
                        value.Length > 0 &&
                        value[0] != '\"' &&
                        value.Last() != '\"' &&
                        value.Contains(' ') &&
                        !value.Contains("\\ ");

                    if (requiresQuotes)
                    {
                        builder.Append('\"');
                    }

                    builder.Append(value);

                    if (requiresQuotes)
                    {
                        builder.Append('\"');
                    }
                }

                return builder.ToString();
            })
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
