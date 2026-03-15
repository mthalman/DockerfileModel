using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class TokenBuilderTests
{
    [Fact]
    public void BuildAllTokens()
    {
        TokenBuilder builder = new();
        builder
            .Comment("comment")
            .ExecFormCommand("cmd1", "cmd2")
            .FromFlag("stage")
            .ImageName("repo")
            .IntervalFlag("3m")
            .KeyValue(new KeywordToken("key"), new LiteralToken("value"))
            .Keyword("key")
            .LineContinuation()
            .Literal("literal")
            .MountFlag(Mount.Parse("type=secret,id=id"))
            .NewLine()
            .PlatformFlag("platform")
            .RetriesFlag("2")
            .Mount("type=secret,id=id")
            .ShellFormCommand("cmd")
            .StageName("stage")
            .StartIntervalFlag("5s")
            .StartPeriodFlag("1s")
            .Symbol('-')
            .TimeoutFlag("2h")
            .Variable("myvar")
            .VariableRef("var")
            .Whitespace(" ");

        Assert.Collection(builder.Tokens, new Action<Token>[]
        {
            token => ValidateAggregate<CommentToken>(token, "#comment",
                token => ValidateSymbol(token, '#'),
                token => ValidateString(token, "comment")),
            token => ValidateAggregate<ExecFormCommand>(token, "[\"cmd1\", \"cmd2\"]",
                token => ValidateSymbol(token, '['),
                token => ValidateLiteral(token, "cmd1", ParseHelper.DoubleQuote),
                token => ValidateSymbol(token, ','),
                token => ValidateWhitespace(token, " "),
                token => ValidateLiteral(token, "cmd2", ParseHelper.DoubleQuote),
                token => ValidateSymbol(token, ']')),
            token => ValidateAggregate<FromFlag>(token, $"--from=stage",
                token => ValidateSymbol(token, '-'),
                token => ValidateSymbol(token, '-'),
                token => ValidateKeyword(token, "from"),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, "stage")),
            token => ValidateAggregate<ImageName>(token, "repo",
                token => {
                    Assert.Equal("repo", ((LiteralToken)token).Value);
                }),
            token => ValidateKeyValueFlag<IntervalFlag>(token, "interval", "3m"),
            token => ValidateKeyValue(token, "key", "value"),
            token => ValidateKeyword(token, "key"),
            token => ValidateLineContinuation(token, '\\', Environment.NewLine),
            token => ValidateLiteral(token, "literal"),
            token => ValidateAggregate<MountFlag>(token, "--mount=type=secret,id=id",
                token => ValidateSymbol(token, '-'),
                token => ValidateSymbol(token, '-'),
                token => ValidateKeyword(token, "mount"),
                token => ValidateSymbol(token, '='),
                token => ValidateAggregate<Mount>(token, "type=secret,id=id",
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "id", "id"))),
            token => ValidateNewLine(token, Environment.NewLine),
            token => ValidateAggregate<PlatformFlag>(token, "--platform=platform",
                token => ValidateSymbol(token, '-'),
                token => ValidateSymbol(token, '-'),
                token => ValidateKeyword(token, "platform"),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, "platform")),
            token => ValidateKeyValueFlag<RetriesFlag>(token, "retries", "2"),
            token => ValidateAggregate<Mount>(token, "type=secret,id=id",
                token => ValidateKeyValue(token, "type", "secret"),
                token => ValidateSymbol(token, ','),
                token => ValidateKeyValue(token, "id", "id")),
            token => ValidateAggregate<ShellFormCommand>(token, "cmd",
                token => ValidateLiteral(token, "cmd")),
            token => ValidateIdentifier<StageName>(token, "stage"),
            token => ValidateKeyValueFlag<StartIntervalFlag>(token, "start-interval", "5s"),
            token => ValidateKeyValueFlag<StartPeriodFlag>(token, "start-period", "1s"),
            token => ValidateSymbol(token, '-'),
            token => ValidateKeyValueFlag<TimeoutFlag>(token, "timeout", "2h"),
            token => ValidateIdentifier<Variable>(token, "myvar"),
            token => ValidateAggregate<VariableRefToken>(token, "$var",
                token => ValidateString(token, "var")),
            token => ValidateWhitespace(token, " ")
        });

        string expectedResult =
            "#comment" +
            "[\"cmd1\", \"cmd2\"]" +
            "--from=stage" +
            "repo" +
            "--interval=3m" +
            "key=value" +
            "key" +
            "\\" + Environment.NewLine +
            "literal" +
            "--mount=type=secret,id=id" +
            Environment.NewLine +
            "--platform=platform" +
            "--retries=2" +
            "type=secret,id=id" +
            "cmd" +
            "stage" +
            "--start-interval=5s" +
            "--start-period=1s" +
            "-" +
            "--timeout=2h" +
            "myvar" +
            "$var" +
            " ";

        Assert.Equal(expectedResult, builder.ToString());
    }

    [Fact]
    public void DefaultNewLine()
    {
        TokenBuilder builder = new()
        {
            DefaultNewLine = "\n"
        };

        string result = builder
            .NewLine()
            .LineContinuation()
            .ToString();

        string expectedResult = "\n\\\n";
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void EscapeChar()
    {
        TokenBuilder builder = new()
        {
            EscapeChar = '`'
        };

        string result = builder
            .LineContinuation()
            .ToString();

        string expectedResult = "`" + Environment.NewLine;
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void ActionBasedFlagBuildersPreserveEscapeChar()
    {
        TokenBuilder builder = new()
        {
            EscapeChar = '`'
        };

        builder.IntervalFlag(tokens =>
            tokens
                .Symbol('-')
                .Symbol('-')
                .Keyword("interval")
                .Symbol('='));

        IntervalFlag flag = Assert.IsType<IntervalFlag>(builder.Tokens.Single());
        Assert.Null(flag.ValueToken);

        flag.Value = "`$MY_VAR";

        Assert.Equal("`$MY_VAR", flag.Value);
        Assert.Equal("--interval=`$MY_VAR", flag.ToString());
        Assert.DoesNotContain(flag.ValueToken!.Tokens, token => token is VariableRefToken);
    }
}
