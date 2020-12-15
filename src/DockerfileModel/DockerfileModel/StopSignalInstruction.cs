using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class StopSignalInstruction : Instruction
    {
        public StopSignalInstruction(string signal, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(signal, escapeChar))
        {
        }

        private StopSignalInstruction(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public string Signal
        {
            get => SignalToken.Value;
            set
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                SignalToken.Value = value;
            }
        }

        public LiteralToken SignalToken
        {
            get => Tokens.OfType<LiteralToken>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(SignalToken, value);
            }
        }
   
        public static StopSignalInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new StopSignalInstruction(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<StopSignalInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new StopSignalInstruction(tokens);

        private static IEnumerable<Token> GetTokens(string signal, char escapeChar)
        {
            Requires.NotNullOrEmpty(signal, nameof(signal));
            return GetTokens($"STOPSIGNAL {signal}", GetInnerParser(escapeChar));
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Instruction("STOPSIGNAL", escapeChar, GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            ArgTokens(
                LiteralToken(escapeChar, Enumerable.Empty<char>()).AsEnumerable(), escapeChar, excludeTrailingWhitespace: true);
    }
}
