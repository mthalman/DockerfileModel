using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class EnvInstruction : Instruction
    {
        public EnvInstruction(IDictionary<string, string> variables, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(variables, escapeChar))
        {
        }

        private EnvInstruction(IEnumerable<Token> tokens) : base(tokens)
        {
            VariableTokens = new TokenList<KeyValueToken<Variable, LiteralToken>>(TokenList);
            Variables = new ProjectedItemList<KeyValueToken<Variable, LiteralToken>, IKeyValuePair>(
                VariableTokens,
                token => token,
                (token, keyValuePair) =>
                {
                    Requires.NotNull(keyValuePair, "value");
                    token.Key = keyValuePair.Key;
                    token.Value = keyValuePair.Value;
                });
        }

        public IList<IKeyValuePair> Variables { get; }

        public IList<KeyValueToken<Variable, LiteralToken>> VariableTokens { get; }

        public static EnvInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new EnvInstruction(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<EnvInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new EnvInstruction(tokens);

        private static IEnumerable<Token> GetTokens(IDictionary<string, string> variables, char escapeChar)
        {
            Requires.NotNullOrEmpty(variables, nameof(variables));

            string[] keyValueAssignments = variables
                .Select(kvp =>
                {
                    string value = kvp.Value;
                    if (value[0] != '\"' && value.Last() != '\"' && value.Contains(' ') && !value.Contains("\\ "))
                    {
                        value = "\"" + value + "\"";
                    }

                    return $"{kvp.Key}={value}";
                })
                .ToArray();

            return GetTokens($"ENV {string.Join(" ", keyValueAssignments)}", GetInnerParser(escapeChar));
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            Instruction("ENV", escapeChar,
                GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            from whitespace in Whitespace().Optional()
            from variables in MultiVariableFormat(escapeChar).Or(SingleVariableFormat(escapeChar))
            select ConcatTokens(whitespace.GetOrDefault(), variables);

        private static Parser<IEnumerable<Token>> MultiVariableFormat(char escapeChar) =>
            ArgTokens(
                from whitespace in Whitespace().Optional()
                from variable in KeyValueToken<Variable, LiteralToken>.GetParser(
                    Variable.GetParser(escapeChar),
                    MultiVariableFormatValueParser(escapeChar),
                    escapeChar: escapeChar).AsEnumerable()
                select ConcatTokens(whitespace.GetOrDefault(), variable), escapeChar
            ).AtLeastOnce().Flatten();

        private static Parser<LiteralToken> MultiVariableFormatValueParser(char escapeChar) =>
            from literal in LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.AllowedInQuotes).Optional()
            select literal.GetOrElse(new LiteralToken("", canContainVariables: true, escapeChar));

        private static Parser<IEnumerable<Token>> SingleVariableFormat(char escapeChar) =>
            ArgTokens(
                KeyValueToken<Variable, LiteralToken>.GetParser(
                    Variable.GetParser(escapeChar),
                    LiteralWithVariables(escapeChar),
                    separator: ' ',
                    escapeChar: escapeChar).AsEnumerable(), escapeChar);
    }
}
