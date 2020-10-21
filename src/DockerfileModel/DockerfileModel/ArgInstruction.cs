using System.Collections.Generic;
using System.Linq;
using System.Text;
using DockerfileModel.Tokens;
using Sprache;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ArgInstruction : InstructionBase
    {
        private ArgInstruction(string text, char escapeChar)
            : base(text, GetParser(escapeChar))
        {
        }

        public string ArgName
        {
            get => this.Tokens.OfType<IdentifierToken>().First().Value;
            set { this.Tokens.OfType<IdentifierToken>().First().Value = value; }
        }

        public string? ArgValue
        {
            get
            {
                string? argValue = this.Tokens.OfType<ArgValue>().FirstOrDefault()?.Value;
                if (argValue is null)
                {
                    return Tokens.OfType<SymbolToken>().Where(token => token.Value == "=").Any() ? string.Empty : null;
                }

                return argValue;
            }
            set
            {
                ArgValue? argValue = this.Tokens.OfType<ArgValue>().FirstOrDefault();
                if (argValue != null)
                {
                    if (value is null)
                    {
                        this.TokenList.RemoveRange(this.TokenList.IndexOf(argValue) - 1, 2);
                    }
                    else
                    {
                        argValue.ReplaceWithToken(new LiteralToken(value));
                    }
                }
                else if (value != null)
                {
                    this.TokenList.AddRange(new Token[]
                    {
                        new SymbolToken("="),
                        new ArgValue(new Token[]
                        {
                            new LiteralToken(value)
                        })
                    });
                }
            }
        }

        public static ArgInstruction Parse(string text, char escapeChar) =>
            new ArgInstruction(text, escapeChar);

        public static ArgInstruction Create(string argName, string? argValue = null)
        {
            StringBuilder builder = new StringBuilder($"ARG {argName}");
            if (argValue != null)
            {
                builder.Append($"={argValue}");
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
            from assignment in Symbol("=")
            from value in LiteralAggregate(escapeChar, false, tokens => new ArgValue(tokens)).Optional()
            select ConcatTokens(
                assignment,
                value.GetOrDefault());
    }

    public class ArgValue : LiteralToken
    {
        public ArgValue(IEnumerable<Token> tokens) : base(tokens)
        {
        }
    }
}
