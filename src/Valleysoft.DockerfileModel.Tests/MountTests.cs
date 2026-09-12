using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class MountTests
{
    [Theory]
    [InlineData("type=bind ,target=/src")]
    [InlineData("type=bind, target=/src")]
    [InlineData("type=bind,\ttarget=/src")]
    [InlineData("from=build , target=/src")]
    [InlineData("  from=build ,\ttype=bind, target=/src  ")]
    public void Parse_StandaloneCommaWhitespaceIsPreserved(string text)
    {
        Mount mount = Mount.Parse(text);

        Assert.Equal(text, mount.ToString());
        Assert.Equal(text, Mount.GetParser().End().Parse(text).ToString());
        Assert.Equal("/src", mount.Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>()
            .Single(token => token.Key == "target").Value);
        Assert.Equal("bind", mount.Type);
    }

    [Theory]
    [InlineData("type=bind garbage")]
    [InlineData("type=bind, target=/src garbage")]
    [InlineData("type=bind ,target=/src,")]
    public void Parse_StandaloneUnconsumedTextIsRejected(string text) =>
        Assert.Throws<ParseException>(() => Mount.Parse(text));

    [Theory]
    [InlineData("source=,target=/src")]
    [InlineData("source=,type=bind,target=/src")]
    [InlineData("type=bind,source=,target=/src")]
    [InlineData("target=/src,source=")]
    [InlineData("source=")]
    [InlineData("source=\\\n,target=/src")]
    public void Parse_EmptyEntryValue(string text)
    {
        Mount mount = Mount.Parse(text);
        var source = mount.Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>()
            .Single(token => token.Key == "source");

        Assert.Equal("", source.Value);
        Assert.Equal("bind", mount.Type);
        Assert.Equal(text, mount.ToString());

        source.Value = "/build";
        Assert.Equal(text.Replace("source=", "source=/build"), mount.ToString());
        Assert.Equal("/build", Mount.Parse(mount.ToString()).Tokens
            .OfType<KeyValueToken<KeywordToken, LiteralToken>>()
            .Single(token => token.Key == "source").Value);
    }

    [Theory]
    [InlineData("source=\"unterminated")]
    [InlineData("readonly!invalid")]
    [InlineData("source=,")]
    [InlineData("source\\\n=\"unterminated")]
    [InlineData("readonly\\\n!invalid")]
    public void Parse_IncompleteEntryDoesNotReturnPartialMount(string text) =>
        Assert.Throws<ParseException>(() => Mount.Parse(text));

    [Theory]
    [InlineData("type=bind,from=build,target=/src", "bind", true)]
    [InlineData("from=build,type=bind,target=/src", "bind", true)]
    [InlineData("from=build,target=/src", "bind", false)]
    [InlineData("from=build,target=/src,type=cache", "cache", true)]
    [InlineData("readonly,from=build,target=/src", "bind", false)]
    public void Parse_TypeEntry(string text, string expectedType, bool hasType)
    {
        Mount mount = Mount.Parse(text);
        Token[] originalTokens = mount.Tokens.ToArray();

        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(expectedType, mount.Type);
            Assert.Equal(hasType, mount.TypeToken is not null);
            if (hasType)
            {
                Assert.Equal("type", mount.TypeToken!.Key);
            }
            Assert.Equal(text, mount.ToString());
            Assert.Equal(originalTokens, mount.Tokens);
        }

        Assert.Equal("build", mount.Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>()
            .Single(token => token.Key == "from").Value);
        Assert.Equal("/src", mount.Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>()
            .Single(token => token.Key == "target").Value);

        MountFlag flag = MountFlag.Parse($"--mount={text}");
        Assert.Equal(expectedType, flag.ValueToken!.Type);
        Assert.Equal($"--mount={text}", flag.ToString());
    }

    [Theory]
    [InlineData("from=build,type=bind,target=/src", "from=build,type=cache,target=/src")]
    [InlineData("type=bind,from=build,target=/src", "type=cache,from=build,target=/src")]
    [InlineData("from=build,target=/src", "type=cache,from=build,target=/src")]
    [InlineData("readonly,target=/src", "type=cache,readonly,target=/src")]
    [InlineData("  from=build,target=/src", "  type=cache,from=build,target=/src")]
    [InlineData("from=build,\\\n# comment\n  target=\"/src\"", "type=cache,from=build,\\\n# comment\n  target=\"/src\"")]
    [InlineData("from=build,\\\n# comment\n  ty\\\npe=bind,target='/src'", "from=build,\\\n# comment\n  ty\\\npe=cache,target='/src'")]
    public void TypeMutation(string text, string expected)
    {
        Mount mount = Mount.Parse(text);
        Token[] otherEntries = mount.Tokens.Where(token =>
            token is KeywordToken ||
            token is KeyValueToken<KeywordToken, LiteralToken> pair && pair.Key != "type").ToArray();

        mount.Type = "cache";

        Assert.Equal("cache", mount.Type);
        Assert.Equal(expected, mount.ToString());
        Assert.Equal("cache", Mount.Parse(mount.ToString()).Type);
        Assert.All(otherEntries, entry => Assert.Contains(entry, mount.Tokens));
    }

    [Theory]
    [InlineData("from=build,type=bind,target=/src", "from=build,type=cache,target=/src")]
    [InlineData("from=build,target=/src,type=bind", "from=build,target=/src,type=cache")]
    [InlineData("from=build,target=/src", "type=cache,from=build,target=/src")]
    public void TypeTokenMutation(string text, string expected)
    {
        Mount mount = Mount.Parse(text);
        var replacement = new KeyValueToken<KeywordToken, LiteralToken>(
            new KeywordToken("type"), new LiteralToken("cache"));

        mount.TypeToken = replacement;

        Assert.Same(replacement, mount.TypeToken);
        Assert.Equal("cache", mount.Type);
        Assert.Equal(expected, mount.ToString());
        Assert.Equal("cache", Mount.Parse(mount.ToString()).Type);
    }

    [Fact]
    public void TypeMutation_ExplicitBindIsInserted()
    {
        Mount mount = Mount.Parse("target=/src");
        mount.Type = "bind";
        Assert.Equal("type=bind,target=/src", mount.ToString());
        Assert.NotNull(mount.TypeToken);
    }

    [Theory]
    [InlineData("type=bind,target=/src")]
    [InlineData("target=/src")]
    public void TypeMutation_RejectsInvalidValues(string text)
    {
        Mount mount = Mount.Parse(text);
        Assert.Throws<ArgumentNullException>(() => mount.Type = null!);
        Assert.Throws<ArgumentException>(() => mount.Type = "");
        Assert.Throws<ArgumentNullException>(() => mount.TypeToken = null!);
        Assert.Equal(text, mount.ToString());
    }

    [Theory]
    [InlineData('\\')]
    [InlineData('`')]
    public void TypeMutation_PreservesEscapeCharacter(char escapeChar)
    {
        Mount mount = Mount.Parse("target=/src", escapeChar);
        mount.Type = $"ca{escapeChar}\nche";

        Assert.Equal("cache", mount.Type);
        Assert.Equal($"type=ca{escapeChar}\nche,target=/src", mount.ToString());
        Assert.Equal("cache", Mount.Parse(mount.ToString(), escapeChar).Type);
    }

    [Theory]
    [InlineData('\\')]
    [InlineData('`')]
    public void MountFlag_TypeMutationPreservesEscapeCharacter(char escapeChar)
    {
        MountFlag flag = MountFlag.Parse($"--mount={escapeChar}\nfrom=build,target=/src", escapeChar);
        Mount mount = flag.ValueToken!;
        mount.Type = $"ca{escapeChar}\nche";

        Assert.Equal("cache", mount.Type);
        Assert.Equal($"--mount={escapeChar}\ntype=ca{escapeChar}\nche,from=build,target=/src", flag.ToString());
        Assert.Equal("cache", MountFlag.Parse(flag.ToString(), escapeChar).ValueToken!.Type);
    }

    [Theory]
    [InlineData("from=build,\\\n# comment\n  target=/src", '\\', "bind")]
    [InlineData("from=build,`\n# comment\n  target=/src,t`\nype=cache", '`', "cache")]
    [InlineData("readonly,\\\n  type=bind,target=\"/src\"", '\\', "bind")]
    [InlineData("target='/src',type=$mountType", '\\', "$mountType")]
    [InlineData("target=/src,TYPE=cache", '\\', "cache")]
    public void Parse_TypeEntryFormatting(string text, char escapeChar, string expectedType)
    {
        Mount mount = Mount.Parse(text, escapeChar);
        Assert.Equal(expectedType, mount.Type);
        Assert.Equal(text, mount.ToString());

        mount.Type = "tmpfs";

        Assert.Equal("tmpfs", mount.Type);
        Assert.Equal("tmpfs", Mount.Parse(mount.ToString(), escapeChar).Type);
    }

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
