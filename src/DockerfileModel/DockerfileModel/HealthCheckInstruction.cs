using System.Collections.Generic;
using System.Linq;
using System.Text;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class HealthCheckInstruction : Instruction
    {
        private readonly char escapeChar;

        public HealthCheckInstruction(string commandWithArgs, string? interval = null, string? timeout = null,
            string? startPeriod = null, string? retries = null, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(new CommandInstruction(commandWithArgs), interval, timeout, startPeriod, retries, escapeChar), escapeChar)
        {
        }

        public HealthCheckInstruction(IEnumerable<string> defaultArgs, string? interval = null, string? timeout = null,
            string? startPeriod = null, string? retries = null, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(new CommandInstruction(defaultArgs), interval, timeout, startPeriod, retries, escapeChar), escapeChar)
        {
        }

        public HealthCheckInstruction(string command, IEnumerable<string> args, string? interval = null, string? timeout = null,
            string? startPeriod = null, string? retries = null, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(new CommandInstruction(command, args), interval, timeout, startPeriod, retries, escapeChar), escapeChar)
        {
        }

        public HealthCheckInstruction(char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(
                new Token[]
                {
                    new KeywordToken("HEALTHCHECK", escapeChar),
                    new WhitespaceToken(" "),
                    new KeywordToken("NONE", escapeChar)
                }, escapeChar)
        {
        }

        private HealthCheckInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens)
        {
            this.escapeChar = escapeChar;
        }

        public string? Interval
        {
            get => IntervalToken?.Value;
            set => SetOptionalLiteralTokenValue(IntervalToken, value, token => IntervalToken = token, canContainVariables: true, escapeChar);
        }

        public LiteralToken? IntervalToken
        {
            get => IntervalFlag?.ValueToken;
            set => SetOptionalKeyValueTokenValue(
                IntervalFlag, value, val => new IntervalFlag(val, escapeChar), token => IntervalFlag = token);
        }

        private IntervalFlag? IntervalFlag
        {
            get => Tokens.OfType<IntervalFlag>().FirstOrDefault();
            set => SetOptionalFlagToken(IntervalFlag, value);
        }

        public string? Timeout
        {
            get => TimeoutToken?.Value;
            set => SetOptionalLiteralTokenValue(TimeoutToken, value, token => TimeoutToken = token, canContainVariables: true, escapeChar);
        }

        public LiteralToken? TimeoutToken
        {
            get => TimeoutFlag?.ValueToken;
            set => SetOptionalKeyValueTokenValue(
                TimeoutFlag, value, val => new TimeoutFlag(val, escapeChar), token => TimeoutFlag = token);
        }

        private TimeoutFlag? TimeoutFlag
        {
            get => Tokens.OfType<TimeoutFlag>().FirstOrDefault();
            set => SetOptionalFlagToken(TimeoutFlag, value);
        }

        public string? StartPeriod
        {
            get => StartPeriodToken?.Value;
            set => SetOptionalLiteralTokenValue(StartPeriodToken, value, token => StartPeriodToken = token, canContainVariables: true, escapeChar);
        }

        public LiteralToken? StartPeriodToken
        {
            get => StartPeriodFlag?.ValueToken;
            set => SetOptionalKeyValueTokenValue(
                StartPeriodFlag, value, val => new StartPeriodFlag(val, escapeChar), token => StartPeriodFlag = token);
        }

        private StartPeriodFlag? StartPeriodFlag
        {
            get => Tokens.OfType<StartPeriodFlag>().FirstOrDefault();
            set => SetOptionalFlagToken(StartPeriodFlag, value);
        }

        public string? Retries
        {
            get => RetriesToken?.Value;
            set => SetOptionalLiteralTokenValue(RetriesToken, value, token => RetriesToken = token, canContainVariables: true, escapeChar);
        }

        public LiteralToken? RetriesToken
        {
            get => RetriesFlag?.ValueToken;
            set => SetOptionalKeyValueTokenValue(
                RetriesFlag, value, val => new RetriesFlag(val, escapeChar), token => RetriesFlag = token);
        }

        private RetriesFlag? RetriesFlag
        {
            get => Tokens.OfType<RetriesFlag>().FirstOrDefault();
            set => SetOptionalFlagToken(RetriesFlag, value);
        }

        public CommandInstruction? CommandInstruction
        {
            get => Tokens.OfType<CommandInstruction>().FirstOrDefault();
            set
            {
                SetToken(CommandInstruction, value,
                    addToken: token =>
                    {
                        // Replace the existing NONE keyword
                        int index = TokenList.IndexOf(Tokens.OfType<KeywordToken>().Last());
                        TokenList[index] = token;
                    },
                    removeToken: token =>
                    {
                        // Replace the CMD instruction
                        int index = TokenList.IndexOf(token);
                        TokenList[index] = new KeywordToken("NONE", escapeChar);
                    });
            }
        }

        public static HealthCheckInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new HealthCheckInstruction(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

        public static Parser<HealthCheckInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new HealthCheckInstruction(tokens, escapeChar);

        private static IEnumerable<Token> GetTokens(CommandInstruction commandInstruction, string? interval, string? timeout,
            string? startPeriod, string? retries, char escapeChar)
        {
            Requires.NotNull(commandInstruction, nameof(commandInstruction));
            return GetTokens(
                $"HEALTHCHECK {GetOptionArgs(interval, timeout, startPeriod, retries, escapeChar)}{commandInstruction}", GetInnerParser(escapeChar));
        }

        private static string GetOptionArgs(string? interval, string? timeout, string? startPeriod, string? retries, char escapeChar)
        {
            StringBuilder builder = new StringBuilder();
            if (interval is not null)
            {
                builder.Append($"{new IntervalFlag(interval, escapeChar)} ");
            }
            if (timeout is not null)
            {
                builder.Append($"{new TimeoutFlag(timeout, escapeChar)} ");
            }
            if (startPeriod is not null)
            {
                builder.Append($"{new StartPeriodFlag(startPeriod, escapeChar)} ");
            }
            if (retries is not null)
            {
                builder.Append($"{new RetriesFlag(retries, escapeChar)} ");
            }

            return builder.ToString();
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            Instruction("HEALTHCHECK", escapeChar,
                GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            from options in Options(escapeChar)
            from command in ArgTokens(CommandInstruction.GetParser(escapeChar).AsEnumerable(), escapeChar)
                .Or(ArgTokens(KeywordToken.GetParser("NONE", escapeChar).AsEnumerable(), escapeChar))
            select ConcatTokens(options, command);

        private static Parser<IEnumerable<Token>> Options(char escapeChar) =>
            ArgTokens(
                IntervalFlag.GetParser(escapeChar).Cast<IntervalFlag, Token>()
                    .Or(TimeoutFlag.GetParser(escapeChar))
                    .Or(StartPeriodFlag.GetParser(escapeChar))
                    .Or(RetriesFlag.GetParser(escapeChar)).AsEnumerable(),
                escapeChar)
                .Many().Flatten();
    }
}
