using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ExposeInstruction : Instruction
    {
        public ExposeInstruction(int port, string? protocol = null, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(port, protocol, escapeChar))
        {
        }

        private ExposeInstruction(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public string Port
        {
            get => PortToken.Value;
            set => PortToken.Value = value.ToString();
        }

        public LiteralToken PortToken
        {
            get => Tokens.OfType<LiteralToken>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(PortToken, value);
            }
        }

        public string? Protocol
        {
            get => ProtocolToken?.Value;
            set
            {
                LiteralToken? protocol = ProtocolToken;
                if (protocol != null && value is not null)
                {
                    protocol.Value = value;
                }
                else
                {
                    ProtocolToken = String.IsNullOrEmpty(value) ? null : new LiteralToken(value!);
                }
            }
        }

        public LiteralToken? ProtocolToken
        {
            get => Tokens.OfType<LiteralToken>().Skip(1).FirstOrDefault();
            set
            {
                SetToken(ProtocolToken, value,
                    addToken: token =>
                    {
                        TokenList.Add(new SymbolToken('/'));
                        TokenList.Add(token);
                    },
                    removeToken: token =>
                    {
                        TokenList.RemoveRange(
                            TokenList.FirstPreviousOfType<Token, SymbolToken>(token),
                            token);
                    });
            }
        }

        public static ExposeInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new ExposeInstruction(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<ExposeInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new ExposeInstruction(tokens);

        private static IEnumerable<Token> GetTokens(int port, string? protocol, char escapeChar)
        {
            string protocolSegment = protocol is null ? string.Empty : $"/{protocol}";
            return GetTokens($"EXPOSE {port}{protocolSegment}", GetInnerParser(escapeChar));
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Instruction("EXPOSE", escapeChar,
                GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            from port in ArgTokens(LiteralAggregate(escapeChar, new char[] { '/' }).AsEnumerable(), escapeChar)
            from protocolTokens in 
                (from separator in ArgTokens(Symbol('/').AsEnumerable(), escapeChar)
                from protocol in ArgTokens(LiteralAggregate(escapeChar).AsEnumerable(), escapeChar)
                select ConcatTokens(separator, protocol)).Optional()
            select ConcatTokens(port, protocolTokens.GetOrDefault());
    }
}
