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

        public HealthCheckInstruction(string command, string? interval = null, string? timeout = null,
            string? startPeriod = null, string? retries = null, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(command, interval, timeout, startPeriod, retries, escapeChar), escapeChar)
        {
        }

        public HealthCheckInstruction(IEnumerable<string> commands, string? interval = null, string? timeout = null,
            string? startPeriod = null, string? retries = null, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(commands, interval, timeout, startPeriod, retries, escapeChar), escapeChar)
        {
        }

        public HealthCheckInstruction(char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(
                new Token[]
                {
                    new KeywordToken("HEALTHCHECK"),
                    new WhitespaceToken(" "),
                    new KeywordToken("NONE")
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
                IntervalFlag, value, val => new IntervalFlag(val), token => IntervalFlag = token);
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
                TimeoutFlag, value, val => new TimeoutFlag(val), token => TimeoutFlag = token);
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
                StartPeriodFlag, value, val => new StartPeriodFlag(val), token => StartPeriodFlag = token);
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
                RetriesFlag, value, val => new RetriesFlag(val), token => RetriesFlag = token);
        }

        private RetriesFlag? RetriesFlag
        {
            get => Tokens.OfType<RetriesFlag>().FirstOrDefault();
            set => SetOptionalFlagToken(RetriesFlag, value);
        }

        public Command? Command
        {
            get => Tokens.OfType<Command>().FirstOrDefault();
            set
            {
                SetToken(Command, value,
                    addToken: token =>
                    {
                        // Replace the existing NONE keyword
                        int index = TokenList.IndexOf(Tokens.OfType<KeywordToken>().Last());
                        TokenList[index] = new KeywordToken("CMD");
                        TokenList.InsertRange(index + 1, new Token[]
                        {
                            new WhitespaceToken(" "),
                            token
                        });
                    },
                    removeToken: token =>
                    {
                        TokenList.RemoveRange(
                            TokenList.FirstPreviousOfType<Token, WhitespaceToken>(token),
                            token);
                        // Replace the CMD keyword
                        int index = TokenList.IndexOf(Tokens.OfType<KeywordToken>().Last());
                        TokenList[index] = new KeywordToken("NONE");
                    });
            }
        }

        public static HealthCheckInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new HealthCheckInstruction(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

        public static Parser<HealthCheckInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new HealthCheckInstruction(tokens, escapeChar);

        private static IEnumerable<Token> GetTokens(string command, string? interval, string? timeout,
            string? startPeriod, string? retries, char escapeChar)
        {
            Requires.NotNullOrEmpty(command, nameof(command));
            return GetTokens(
                $"HEALTHCHECK {GetOptionArgs(interval, timeout, startPeriod, retries)}CMD {command}", GetInnerParser(escapeChar));
        }

        private static IEnumerable<Token> GetTokens(IEnumerable<string> commands, string? interval, string? timeout,
            string? startPeriod, string? retries, char escapeChar)
        {
            Requires.NotNullOrEmpty(commands, nameof(commands));
            return GetTokens(
                $"HEALTHCHECK {GetOptionArgs(interval, timeout, startPeriod, retries)}CMD {StringHelper.FormatAsJson(commands)}", GetInnerParser(escapeChar));
        }

        private static string GetOptionArgs(string? interval, string? timeout, string? startPeriod, string? retries)
        {
            StringBuilder builder = new StringBuilder();
            if (interval is not null)
            {
                builder.Append($"{new IntervalFlag(interval)} ");
            }
            if (timeout is not null)
            {
                builder.Append($"{new TimeoutFlag(timeout)} ");
            }
            if (startPeriod is not null)
            {
                builder.Append($"{new StartPeriodFlag(startPeriod)} ");
            }
            if (retries is not null)
            {
                builder.Append($"{new RetriesFlag(retries)} ");
            }

            return builder.ToString();
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Instruction("HEALTHCHECK", escapeChar,
                GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            from options in Options(escapeChar)
            from command in CommandInstruction.GetInnerParser(escapeChar)
                .XOr(ArgTokens(KeywordToken.GetParser("NONE", escapeChar).AsEnumerable(), escapeChar))
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
