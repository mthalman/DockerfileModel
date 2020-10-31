using System.Collections.Generic;
using System.Linq;
using System.Text;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ArgInstruction : InstructionBase
    {
        private const char AssignmentOperator = '=';

        private ArgInstruction(string text, char escapeChar)
            : base(text, GetParser(escapeChar))
        {
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
                    ArgValueToken = value is null ? null : new LiteralToken(value);
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
                    removeToken: token => this.TokenList.RemoveRange(this.TokenList.IndexOf(token) - 1, 2));
            }
        }

        public static ArgInstruction Parse(string text, char escapeChar) =>
            new ArgInstruction(text, escapeChar);

        public static ArgInstruction Create(string argName, string? argValue = null)
        {
            StringBuilder builder = new StringBuilder($"ARG {argName}");
            if (argValue != null)
            {
                builder.Append($"{AssignmentOperator}{argValue}");
            }

            return Parse(builder.ToString(), Instruction.DefaultEscapeChar);
        }

        public static Parser<IEnumerable<Token>> GetParser(char escapeChar) =>
            Instruction("ARG", escapeChar, GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            ArgTokens(
                from argName in GetArgNameParser(escapeChar).AsEnumerable()
                from argAssignment in GetArgAssignmentParser(escapeChar).Optional()
                select ConcatTokens(
                    argName,
                    argAssignment.GetOrDefault()), escapeChar).End();

        private static Parser<IdentifierToken> GetArgNameParser(char escapeChar) =>
            IdentifierToken(ArgRefFirstLetterParser, ArgRefTailParser, escapeChar);

        private static Parser<IEnumerable<Token>> GetArgAssignmentParser(char escapeChar) =>
            from assignment in Symbol(AssignmentOperator)
            from value in LiteralAggregate(escapeChar, tokens => new LiteralToken(tokens)).Optional()
            select ConcatTokens(
                assignment,
                value.GetOrDefault());
    }
}
