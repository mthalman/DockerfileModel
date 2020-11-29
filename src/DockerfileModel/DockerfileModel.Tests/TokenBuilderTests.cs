using System;
using DockerfileModel.Tokens;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class TokenBuilderTests
    {
        [Fact]
        public void BuildAllTokens()
        {
            TokenBuilder builder = new TokenBuilder();
            builder
                .ChangeOwner("user")
                .Comment("comment")
                .Digest("digest")
                .ExecFormCommand("cmd1", "cmd2")
                .FromFlag("stage")
                .Identifier("id")
                .ImageName("repo")
                .IntervalFlag("3m")
                .KeyValue(new KeywordToken("key"), new LiteralToken("value"))
                .Keyword("key")
                .LineContinuation()
                .Literal("literal")
                .MountFlag(new SecretMount("id"))
                .NewLine()
                .PlatformFlag("platform")
                .Registry("registry")
                .Repository("repo")
                .RetriesFlag("2")
                .SecretMount("id")
                .ShellFormCommand("cmd")
                .StartPeriodFlag("1s")
                .Symbol('-')
                .Tag("tag")
                .TimeoutFlag("2h")
                .VariableRef("var")
                .Whitespace(" ");

            Assert.Collection(builder.Tokens, new Action<Token>[]
            {
                token => ValidateAggregate<ChangeOwner>(token, "user",
                    token => ValidateLiteral(token, "user")),
                token => ValidateAggregate<CommentToken>(token, "#comment",
                    token => ValidateSymbol(token, '#'),
                    token => ValidateString(token, "comment")),
                token => ValidateAggregate<DigestToken>(token, "digest",
                    token => ValidateString(token, "digest")),
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
                    token => ValidateIdentifier(token, "stage")),
                token => ValidateIdentifier(token, "id"),
                token => ValidateAggregate<ImageName>(token, "repo",
                    token => ValidateAggregate<RepositoryToken>(token, "repo",
                        token => ValidateString(token, "repo"))),
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
                    token => ValidateAggregate<SecretMount>(token, "type=secret,id=id",
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
                token => ValidateAggregate<RegistryToken>(token, "registry",
                    token => ValidateString(token, "registry")),
                token => ValidateAggregate<RepositoryToken>(token, "repo",
                    token => ValidateString(token, "repo")),
                token => ValidateKeyValueFlag<RetriesFlag>(token, "retries", "2"),
                token => ValidateAggregate<SecretMount>(token, "type=secret,id=id",
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "id", "id")),
                token => ValidateAggregate<ShellFormCommand>(token, "cmd",
                    token => ValidateLiteral(token, "cmd")),
                token => ValidateKeyValueFlag<StartPeriodFlag>(token, "start-period", "1s"),
                token => ValidateSymbol(token, '-'),
                token => ValidateAggregate<TagToken>(token, "tag",
                    token => ValidateString(token, "tag")),
                token => ValidateKeyValueFlag<TimeoutFlag>(token, "timeout", "2h"),
                token => ValidateAggregate<VariableRefToken>(token, "$var",
                    token => ValidateString(token, "var")),
                token => ValidateWhitespace(token, " ")
            });

            string expectedResult =
                "user" +
                "#comment" +
                "digest" +
                "[\"cmd1\", \"cmd2\"]" +
                "--from=stage" +
                "id" +
                "repo" +
                "--interval=3m" +
                "key=value" +
                "key" +
                "\\" + Environment.NewLine +
                "literal" +
                "--mount=type=secret,id=id" +
                Environment.NewLine +
                "--platform=platform" +
                "registry" +
                "repo" +
                "--retries=2" +
                "type=secret,id=id" +
                "cmd" +
                "--start-period=1s" +
                "-" +
                "tag" +
                "--timeout=2h" +
                "$var" +
                " ";

            Assert.Equal(expectedResult, builder.ToString());
        }

        [Fact]
        public void DefaultNewLine()
        {
            TokenBuilder builder = new TokenBuilder
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
            TokenBuilder builder = new TokenBuilder
            {
                EscapeChar = '`'
            };

            string result = builder
                .LineContinuation()
                .ToString();

            string expectedResult = "`" + Environment.NewLine;
            Assert.Equal(expectedResult, result);
        }
    }
}
