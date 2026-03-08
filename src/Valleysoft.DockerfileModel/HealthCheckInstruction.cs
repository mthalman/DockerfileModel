using System.Text;
using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class HealthCheckInstruction : Instruction
{
    private readonly char escapeChar;

    public HealthCheckInstruction(string commandWithArgs, string? interval = null, string? timeout = null,
        string? startPeriod = null, string? startInterval = null, string? retries = null, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(commandWithArgs, interval, timeout, startPeriod, startInterval, retries, escapeChar), escapeChar)
    {
    }

    public HealthCheckInstruction(IEnumerable<string> defaultArgs, string? interval = null, string? timeout = null,
        string? startPeriod = null, string? startInterval = null, string? retries = null, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(ValidateAndFormatAsJson(defaultArgs, nameof(defaultArgs)), interval, timeout, startPeriod, startInterval, retries, escapeChar), escapeChar)
    {
    }

    public HealthCheckInstruction(string command, IEnumerable<string> args, string? interval = null, string? timeout = null,
        string? startPeriod = null, string? startInterval = null, string? retries = null, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(StringHelper.FormatAsJson(new string[] { command }.Concat(args)), interval, timeout, startPeriod, startInterval, retries, escapeChar), escapeChar)
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

    public string? StartInterval
    {
        get => StartIntervalToken?.Value;
        set => SetOptionalLiteralTokenValue(StartIntervalToken, value, token => StartIntervalToken = token, canContainVariables: true, escapeChar);
    }

    public LiteralToken? StartIntervalToken
    {
        get => StartIntervalFlag?.ValueToken;
        set => SetOptionalKeyValueTokenValue(
            StartIntervalFlag, value, val => new StartIntervalFlag(val, escapeChar), token => StartIntervalFlag = token);
    }

    private StartIntervalFlag? StartIntervalFlag
    {
        get => Tokens.OfType<StartIntervalFlag>().FirstOrDefault();
        set => SetOptionalFlagToken(StartIntervalFlag, value);
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

    public Command? Command
    {
        get => Tokens.OfType<Command>().FirstOrDefault();
        set
        {
            Command? current = Command;
            if (current is not null)
            {
                if (value is null)
                {
                    // Remove CMD keyword, whitespace before command, and the command itself.
                    // Find the CMD keyword (the last KeywordToken before the command).
                    KeywordToken? cmdKeyword = Tokens.OfType<KeywordToken>().LastOrDefault(k => k.Value.Equals("CMD", StringComparison.OrdinalIgnoreCase));
                    if (cmdKeyword is not null)
                    {
                        int cmdKeywordIndex = TokenList.IndexOf(cmdKeyword);
                        int commandIndex = TokenList.IndexOf(current);
                        // Remove from CMD keyword up to and including the command token,
                        // preserving any trailing tokens (comments, newlines) after the command.
                        if (cmdKeywordIndex >= 0 && commandIndex >= cmdKeywordIndex)
                        {
                            int removeCount = commandIndex - cmdKeywordIndex + 1;
                            for (int i = 0; i < removeCount; i++)
                            {
                                TokenList.RemoveAt(cmdKeywordIndex);
                            }
                            // Also remove the whitespace before CMD keyword
                            if (cmdKeywordIndex > 0 && TokenList[cmdKeywordIndex - 1] is WhitespaceToken)
                            {
                                TokenList.RemoveAt(cmdKeywordIndex - 1);
                                cmdKeywordIndex--;
                            }
                            // Insert NONE keyword with preceding whitespace at the original CMD position
                            TokenList.Insert(cmdKeywordIndex, new WhitespaceToken(" "));
                            TokenList.Insert(cmdKeywordIndex + 1, new KeywordToken("NONE", escapeChar));
                        }
                    }
                }
                else
                {
                    TokenList[TokenList.IndexOf(current)] = value;
                }
            }
            else if (value is not null)
            {
                // Replace NONE keyword with CMD keyword + whitespace + command
                KeywordToken? noneKeyword = Tokens.OfType<KeywordToken>().LastOrDefault(k => k.Value.Equals("NONE", StringComparison.OrdinalIgnoreCase));
                if (noneKeyword is not null)
                {
                    int noneIndex = TokenList.IndexOf(noneKeyword);
                    // Also remove whitespace before NONE
                    int wsIndex = noneIndex - 1;
                    if (wsIndex >= 0 && TokenList[wsIndex] is WhitespaceToken)
                    {
                        TokenList.RemoveAt(wsIndex);
                        noneIndex--; // adjust after removal
                    }
                    TokenList.RemoveAt(noneIndex);
                    // Insert whitespace + CMD keyword + whitespace + command at the position where NONE was
                    // to preserve ordering of any trailing tokens (comments, newlines)
                    TokenList.Insert(noneIndex, new WhitespaceToken(" "));
                    TokenList.Insert(noneIndex + 1, new KeywordToken("CMD", escapeChar));
                    TokenList.Insert(noneIndex + 2, new WhitespaceToken(" "));
                    TokenList.Insert(noneIndex + 3, value);
                }
            }
        }
    }

    public static HealthCheckInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<HealthCheckInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new HealthCheckInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string commandBody, string? interval, string? timeout,
        string? startPeriod, string? startInterval, string? retries, char escapeChar)
    {
        Requires.NotNullOrEmpty(commandBody, nameof(commandBody));
        return GetTokens(
            $"HEALTHCHECK {GetOptionArgs(interval, timeout, startPeriod, startInterval, retries, escapeChar)}CMD {commandBody}", GetInnerParser(escapeChar));
    }

    private static string GetOptionArgs(string? interval, string? timeout, string? startPeriod, string? startInterval, string? retries, char escapeChar)
    {
        StringBuilder builder = new();
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
        if (startInterval is not null)
        {
            builder.Append($"{new StartIntervalFlag(startInterval, escapeChar)} ");
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
        from command in CmdTokens(escapeChar)
            .Or(ArgTokens(KeywordToken.GetParser("NONE", escapeChar).AsEnumerable(), escapeChar))
        select ConcatTokens(options, command);

    private static Parser<IEnumerable<Token>> CmdTokens(char escapeChar) =>
        from cmdKeyword in ArgTokens(KeywordToken.GetParser("CMD", escapeChar).AsEnumerable(), escapeChar)
        from cmd in ArgTokens(
            ExecFormCommand.GetParser(escapeChar).Cast<ExecFormCommand, Token>()
                .XOr(ShellFormCommand.GetParser(escapeChar).Cast<ShellFormCommand, Token>())
                .AsEnumerable(), escapeChar)
        select ConcatTokens(cmdKeyword, cmd);

    private static Parser<IEnumerable<Token>> Options(char escapeChar) =>
        ArgTokens(
            IntervalFlag.GetParser(escapeChar).Cast<IntervalFlag, Token>()
                .Or(TimeoutFlag.GetParser(escapeChar))
                .Or(StartPeriodFlag.GetParser(escapeChar))
                .Or(StartIntervalFlag.GetParser(escapeChar))
                .Or(RetriesFlag.GetParser(escapeChar)).AsEnumerable(),
            escapeChar)
            .Many().Flatten();

    private static string ValidateAndFormatAsJson(IEnumerable<string> defaultArgs, string paramName)
    {
        Requires.NotNull(defaultArgs, paramName);
        return StringHelper.FormatAsJson(defaultArgs);
    }
}
