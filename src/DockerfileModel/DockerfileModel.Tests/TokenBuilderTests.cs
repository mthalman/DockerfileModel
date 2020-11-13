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
                .ChangeOwnerFlag("user", "group")
                .Comment("comment")
                .Digest("digest")
                .ExecFormCommand("cmd1", "cmd2")
                .Identifier("id")
                .ImageName("repo")
                .KeyValue("key", new LiteralToken("value"))
                .Keyword("key")
                .LineContinuation()
                .Literal("literal")
                .MountFlag(SecretMount.Create("id"))
                .NewLine()
                .PlatformFlag("platform")
                .Registry("registry")
                .Repository("repo")
                .SecretMount("id")
                .ShellFormCommand("cmd")
                .StageName("stage")
                .Symbol('-')
                .Tag("tag")
                .VariableRef("var")
                .Whitespace(" ");

            Assert.Collection(builder.Tokens, new Action<Token>[]
            {
                token => ValidateAggregate<ChangeOwner>(token, "user",
                    token => ValidateLiteral(token, "user")),
                token => ValidateAggregate<ChangeOwnerFlag>(token, "--chown=user:group",
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateAggregate<KeyValueToken<ChangeOwner>>(token, "chown=user:group",
                        token => ValidateKeyword(token, "chown"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<ChangeOwner>(token, "user:group",
                            token => ValidateLiteral(token, "user"),
                            token => ValidateSymbol(token, ':'),
                            token => ValidateLiteral(token, "group")))),
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
                token => ValidateIdentifier(token, "id"),
                token => ValidateAggregate<ImageName>(token, "repo",
                    token => ValidateAggregate<RepositoryToken>(token, "repo",
                        token => ValidateString(token, "repo"))),
                token => ValidateAggregate<KeyValueToken<LiteralToken>>(token, "key=value",
                    token => ValidateKeyword(token, "key"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "value")),
                token => ValidateKeyword(token, "key"),
                token => ValidateLineContinuation(token, '\\', Environment.NewLine),
                token => ValidateLiteral(token, "literal"),
                token => ValidateAggregate<MountFlag>(token, "--mount=type=secret,id=id",
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateAggregate<KeyValueToken<Mount>>(token, "mount=type=secret,id=id",
                        token => ValidateKeyword(token, "mount"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<SecretMount>(token, "type=secret,id=id",
                            token => ValidateKeyValue(token, "type", "secret"),
                            token => ValidateSymbol(token, ','),
                            token => ValidateKeyValue(token, "id", "id")))),
                token => ValidateNewLine(token, Environment.NewLine),
                token => ValidateAggregate<PlatformFlag>(token, "--platform=platform",
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateAggregate<KeyValueToken<LiteralToken>>(token, "platform=platform",
                        token => ValidateKeyword(token, "platform"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "platform"))),
                token => ValidateAggregate<RegistryToken>(token, "registry",
                    token => ValidateString(token, "registry")),
                token => ValidateAggregate<RepositoryToken>(token, "repo",
                    token => ValidateString(token, "repo")),
                token => ValidateAggregate<SecretMount>(token, "type=secret,id=id",
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "id", "id")),
                token => ValidateAggregate<ShellFormCommand>(token, "cmd",
                    token => ValidateLiteral(token, "cmd")),
                token => ValidateAggregate<StageName>(token, "AS stage",
                    token => ValidateKeyword(token, "AS"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateIdentifier(token, "stage")),
                token => ValidateSymbol(token, '-'),
                token => ValidateAggregate<TagToken>(token, "tag",
                    token => ValidateString(token, "tag")),
                token => ValidateAggregate<VariableRefToken>(token, "$var",
                    token => ValidateString(token, "var")),
                token => ValidateWhitespace(token, " ")
            });

            string expectedResult =
                "user" +
                "--chown=user:group" +
                "#comment" +
                "digest" +
                "[\"cmd1\", \"cmd2\"]" +
                "id" +
                "repo" +
                "key=value" +
                "key" +
                "\\" + Environment.NewLine +
                "literal" +
                "--mount=type=secret,id=id" +
                Environment.NewLine +
                "--platform=platform" +
                "registry" +
                "repo" +
                "type=secret,id=id" +
                "cmd" +
                "AS stage" +
                "-" +
                "tag" +
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
