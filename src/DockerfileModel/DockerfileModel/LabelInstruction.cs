using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class LabelInstruction : Instruction
    {
        public LabelInstruction(IDictionary<string, string> labels, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(labels, escapeChar))
        {
        }

        private LabelInstruction(IEnumerable<Token> tokens) : base(tokens)
        {
            LabelTokens = new TokenList<KeyValueToken<LiteralToken, LiteralToken>>(TokenList);
            Labels = new ProjectedItemList<KeyValueToken<LiteralToken, LiteralToken>, IKeyValuePair>(
                LabelTokens,
                token => token,
                (token, keyValuePair) =>
                {
                    Requires.NotNull(keyValuePair, "value");
                    token.Key = keyValuePair.Key;
                    token.Value = keyValuePair.Value;
                });
        }

        public IList<IKeyValuePair> Labels { get; }

        public IList<KeyValueToken<LiteralToken, LiteralToken>> LabelTokens { get; }

   
        public static LabelInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new LabelInstruction(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<LabelInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new LabelInstruction(tokens);

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

            return GetTokens($"LABEL {string.Join(" ", keyValueAssignments)}", GetInnerParser(escapeChar));
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Instruction("LABEL", escapeChar,
                GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            ArgTokens(
                from whitespace in Whitespace().Optional()
                from variable in KeyValueToken<LiteralToken, LiteralToken>.GetParser(
                    LiteralAggregate(escapeChar, excludedChars: new char[] { '=' }, whitespaceMode: WhitespaceMode.AllowedInQuotes),
                    ValueParser(escapeChar),
                    escapeChar: escapeChar).AsEnumerable()
                select ConcatTokens(whitespace.GetOrDefault(), variable), escapeChar
            ).AtLeastOnce().Flatten();

        private static Parser<LiteralToken> ValueParser(char escapeChar) =>
            from literal in LiteralAggregate(escapeChar, whitespaceMode: WhitespaceMode.AllowedInQuotes).Optional()
            select literal.GetOrElse(new LiteralToken(""));
    }
}
