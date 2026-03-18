using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class MountTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<Mount> scenario) =>
        TestHelper.RunParseTest(scenario, Mount.Parse);

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<Mount>[] testInputs = new ParseTestScenario<Mount>[]
        {
            new ParseTestScenario<Mount>
            {
                Text = "type=secret,id=foo",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "id", "foo"),
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("type=secret,id=foo", result.ToString());
                }
            },
            new ParseTestScenario<Mount>
            {
                Text = "type=secret,id=foo,dst=test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "id", "foo"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "dst", "test"),
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("type=secret,id=foo,dst=test", result.ToString());
                }
            },
            new ParseTestScenario<Mount>
            {
                Text = "type=secret,id=foo,env=test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "id", "foo"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "env", "test"),
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("type=secret,id=foo,env=test", result.ToString());
                }
            },
            new ParseTestScenario<Mount>
            {
                EscapeChar = '`',
                Text = "typ`\ne`\n=`\nsecret`\n,`\nid=foo",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateAggregate<KeyValueToken<KeywordToken, LiteralToken>>(token, "typ`\ne`\n=`\nsecret",
                        token => ValidateAggregate<KeywordToken>(token, "typ`\ne",
                            token => ValidateString(token, "typ"),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateString(token, "e")),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateLiteral(token, "secret")),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateKeyValue(token, "id", "foo")
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("typ`\ne`\n=`\nsecret`\n,`\nid=foo", result.ToString());
                }
            },
            new ParseTestScenario<Mount>
            {
                Text = "type=secret,id=$secretid",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateAggregate<KeyValueToken<KeywordToken, LiteralToken>>(token, "id=$secretid",
                        token => ValidateKeyword(token, "id"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<LiteralToken>(token, "$secretid",
                            token => ValidateAggregate<VariableRefToken>(token, "$secretid",
                                token => ValidateString(token, "secretid"))))
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("type=secret,id=$secretid", result.ToString());
                }
            },
            new ParseTestScenario<Mount>
            {
                Text = "type=cache,target=/var/cache",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "cache"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "target", "/var/cache"),
                },
                Validate = result =>
                {
                    Assert.Equal("cache", result.Type);
                    Assert.Equal("type=cache,target=/var/cache", result.ToString());
                }
            },
            new ParseTestScenario<Mount>
            {
                Text = "type=bind,source=/src,target=/tgt",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "bind"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "source", "/src"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "target", "/tgt"),
                },
                Validate = result =>
                {
                    Assert.Equal("bind", result.Type);
                    Assert.Equal("type=bind,source=/src,target=/tgt", result.ToString());
                }
            },
            new ParseTestScenario<Mount>
            {
                Text = "type=tmpfs,target=/tmp",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "tmpfs"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "target", "/tmp"),
                },
                Validate = result =>
                {
                    Assert.Equal("tmpfs", result.Type);
                    Assert.Equal("type=tmpfs,target=/tmp", result.ToString());
                }
            },
            // Bare key tests
            new ParseTestScenario<Mount>
            {
                Text = "type=secret,id=mysecret,required",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "id", "mysecret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyword(token, "required"),
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("type=secret,id=mysecret,required", result.ToString());
                }
            },
            new ParseTestScenario<Mount>
            {
                Text = "type=bind,source=/src,target=/app,readonly",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "bind"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "source", "/src"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "target", "/app"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyword(token, "readonly"),
                },
                Validate = result =>
                {
                    Assert.Equal("bind", result.Type);
                    Assert.Equal("type=bind,source=/src,target=/app,readonly", result.ToString());
                }
            },
            new ParseTestScenario<Mount>
            {
                Text = "type=ssh,required",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "ssh"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyword(token, "required"),
                },
                Validate = result =>
                {
                    Assert.Equal("ssh", result.Type);
                    Assert.Equal("type=ssh,required", result.ToString());
                }
            },
            new ParseTestScenario<Mount>
            {
                Text = "type=secret,id=mysecret,required,mode=0400",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "id", "mysecret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyword(token, "required"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "mode", "0400"),
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("type=secret,id=mysecret,required,mode=0400", result.ToString());
                }
            },
            // Single-key mounts (type=X with no additional pairs).
            // The mount value must NOT include any trailing whitespace — that whitespace
            // belongs to the surrounding instruction context as a separate token.
            new ParseTestScenario<Mount>
            {
                Text = "type=ssh",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "ssh"),
                },
                Validate = result =>
                {
                    Assert.Equal("ssh", result.Type);
                    Assert.Equal("type=ssh", result.ToString());
                }
            },
            new ParseTestScenario<Mount>
            {
                Text = "type=cache",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "cache"),
                },
                Validate = result =>
                {
                    Assert.Equal("cache", result.Type);
                    Assert.Equal("type=cache", result.ToString());
                }
            },
            // Bare keyword after line continuation with indentation whitespace
            new ParseTestScenario<Mount>
            {
                EscapeChar = '\\',
                Text = "type=bind,source=/src,target=/app,\\\n  readonly",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "bind"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "source", "/src"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "target", "/app"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateLineContinuation(token, '\\', "\n"),
                    token => ValidateWhitespace(token, "  "),
                    token => ValidateKeyword(token, "readonly"),
                },
                Validate = result =>
                {
                    Assert.Equal("bind", result.Type);
                    Assert.Equal("type=bind,source=/src,target=/app,\\\n  readonly", result.ToString());
                }
            },
            // Key-value pair after line continuation with indentation whitespace
            new ParseTestScenario<Mount>
            {
                EscapeChar = '\\',
                Text = "type=secret,\\\n  id=mysecret,\\\n  required",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateLineContinuation(token, '\\', "\n"),
                    token => ValidateWhitespace(token, "  "),
                    token => ValidateKeyValue(token, "id", "mysecret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateLineContinuation(token, '\\', "\n"),
                    token => ValidateWhitespace(token, "  "),
                    token => ValidateKeyword(token, "required"),
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("type=secret,\\\n  id=mysecret,\\\n  required", result.ToString());
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }
}
