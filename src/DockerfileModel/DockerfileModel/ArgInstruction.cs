using System.Collections.Generic;
using System.Linq;
using System.Text;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ArgInstruction : Instruction
    {
        private const char AssignmentOperator = '=';
        private readonly char escapeChar;

        public ArgInstruction(string argName, string? argValue = null,
            char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(argName, argValue, escapeChar), escapeChar)
        {
        }

        private ArgInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens)
        {
            this.escapeChar = escapeChar;
        }

        public string ArgName
        {
            get => this.ArgNameToken.Value;
            set
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                ArgNameToken.Value = value;
            }
        }

        public IdentifierToken ArgNameToken
        {
            get => this.Tokens.OfType<IdentifierToken>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(ArgNameToken, value);
            }
        }

        public bool HasAssignmentOperator =>
            Tokens.OfType<SymbolToken>().Where(token => token.Value == AssignmentOperator.ToString()).Any();

        public string? ArgValue
        {
            get
            {
                string? argValue = ArgValueToken?.Value;
                if (argValue is null)
                {
                    return HasAssignmentOperator ? string.Empty : null;
                }

                return argValue;
            }
            set
            {
                LiteralToken? argValue = ArgValueToken;
                if (argValue != null && value is not null)
                {
                    argValue.Value = value;
                }
                else
                {
                    ArgValueToken = value is null ? null : new LiteralToken(value, canContainVariables: true, escapeChar);
                }
            }
        }

        public LiteralToken? ArgValueToken
        {
            get => this.Tokens.OfType<LiteralToken>().FirstOrDefault();
            set
            {
                this.SetToken(ArgValueToken, value,
                    addToken: token =>
                    {
                        if (HasAssignmentOperator)
                        {
                            this.TokenList.Add(token);
                        }
                        else
                        {
                            this.TokenList.AddRange(new Token[]
                            {
                                new SymbolToken(AssignmentOperator),
                                token
                            });
                        }
                    },
                    removeToken: token =>
                    {
                        TokenList.RemoveRange(
                            TokenList.FirstPreviousOfType<Token, SymbolToken>(token),
                            token);
                    });
            }
        }

        public static ArgInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new ArgInstruction(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

        public static Parser<ArgInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new ArgInstruction(tokens, escapeChar);

        private static IEnumerable<Token> GetTokens(string argName, string? argValue, char escapeChar)
        {
            Requires.NotNullOrEmpty(argName, nameof(argName));

            StringBuilder builder = new StringBuilder($"ARG {argName}");
            if (argValue != null)
            {
                builder.Append($"{AssignmentOperator}{argValue}");
            }

            return GetTokens(builder.ToString(), GetInnerParser(escapeChar));
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            Instruction("ARG", escapeChar, GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            ArgTokens(
                from argName in ArgTokens(DockerfileModel.Variable.GetParser(escapeChar).AsEnumerable(), escapeChar)
                from argAssignment in GetArgAssignmentParser(escapeChar).Optional()
                select ConcatTokens(
                    argName,
                    argAssignment.GetOrDefault()), escapeChar).End();

        private static Parser<IEnumerable<Token>> GetArgAssignmentParser(char escapeChar) =>
            from assignment in ArgTokens(Symbol(AssignmentOperator).AsEnumerable(), escapeChar)
            from value in LiteralWithVariables(escapeChar).AsEnumerable().Optional()
            select ConcatTokens(
                assignment,
                value.GetOrDefault());
    }
}
