using System.Text.Json;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>Checks JSON operand edits independently of the Dockerfile parser's permissive string grammar.</summary>
public class JsonStructuralEditingTests
{
    /// <summary>Rejects semantic values whose JSON encoding is not implemented, without changing the owner.</summary>
    /// <param name="value">A semantic value requiring JSON escaping.</param>
    [Theory]
    [InlineData("a\tb")]
    [InlineData(@"a\nb")]
    public void SemanticJsonValuesRejectUnsupportedEscaping(string value)
    {
        ExecFormCommand command = ExecFormCommand.Parse("[\"old\"]");
        LiteralToken original = command.ValueTokens[0];

        Assert.Throws<ArgumentException>(() => command.Values.Add(value));

        Assert.Equal("[\"old\"]", command.ToString());
        Assert.Same(original, command.ValueTokens[0]);
    }

    /// <summary>Rejects invalid typed JSON syntax without changing incoming quotes or owner tokens.</summary>
    /// <param name="value">An invalid JSON string body accepted by the legacy literal constructor.</param>
    [Theory]
    [InlineData("a\tb")]
    [InlineData(@"a\qb")]
    public void TypedJsonValuesRejectInvalidSyntax(string value)
    {
        ExecFormCommand command = ExecFormCommand.Parse("[\"old\"]");
        LiteralToken incoming = new(value);
        string before = incoming.ToString();
        Token[] children = incoming.Tokens.ToArray();

        Assert.Throws<InvalidOperationException>(() => command.ValueTokens.Add(incoming));

        Assert.Equal("[\"old\"]", command.ToString());
        Assert.Equal(before, incoming.ToString());
        Assert.Null(incoming.QuoteChar);
        Assert.Equal(children, incoming.Tokens);
    }

    /// <summary>Preserves a typed JSON escape as syntax rather than treating it as a semantic backslash.</summary>
    [Fact]
    public void TypedJsonEscapePreservesSyntaxAndIdentity()
    {
        ExecFormCommand command = ExecFormCommand.Parse("[\"old\"]");
        LiteralToken incoming = new(@"a\nb");

        command.ValueTokens.Add(incoming);

        Assert.Same(incoming, command.ValueTokens[1]);
        Assert.Equal("\"a\\nb\"", incoming.ToString());
        Assert.Equal("a\nb", JsonSerializer.Deserialize<string[]>(command.ToString())![1]);
    }

    /// <summary>Supplies the JSON collection families in both Dockerfile escape contexts.</summary>
    /// <returns>Owner kinds and active Dockerfile escape characters.</returns>
    public static IEnumerable<object[]> JsonOwners()
    {
        foreach (string kind in new[] { "exec", "copy", "add", "volume" })
        {
            foreach (char escape in new[] { '\\', '`' })
            {
                yield return new object[] { kind, escape };
            }
        }
    }

    /// <summary>Exercises every semantic insertion and replacement route, including standard interfaces and both policies.</summary>
    /// <param name="kind">The JSON collection's owner kind.</param>
    /// <param name="escape">The owner's Dockerfile escape character.</param>
    [Theory]
    [MemberData(nameof(JsonOwners))]
    public void AllSemanticWriteRoutesRejectEscapingAtomically(string kind, char escape)
    {
        string[] invalid = Enumerable.Range(0, 32).Select(value => $"a{(char)value}b")
            .Concat(new[] { @"a\nb", @"a\\b", "a\"b" }).ToArray();
        foreach (string operation in WriteOperations)
        {
            foreach (string value in invalid)
            {
                var (owner, values, tokens) = CreateOwner(kind, escape);
                string before = owner.ToString();
                LiteralToken[] original = tokens.ToArray();
                Token[][] children = original.Select(token => token.Tokens.ToArray()).ToArray();

                Assert.Throws<ArgumentException>(() => Write(values, value, operation));

                Assert.Equal(before, owner.ToString());
                Assert.Equal(original, tokens);
                for (int index = 0; index < original.Length; index++)
                {
                    Assert.Equal(children[index], original[index].Tokens);
                }
            }
        }
    }

    /// <summary>Rejects controls, malformed escapes, extra JSON values, and extra physical constructs on every typed write route.</summary>
    /// <param name="kind">The JSON collection's owner kind.</param>
    /// <param name="escape">The owner's Dockerfile escape character.</param>
    [Theory]
    [MemberData(nameof(JsonOwners))]
    public void AllTypedWriteRoutesRejectInvalidSyntaxAtomically(string kind, char escape)
    {
        string[] invalid = Enumerable.Range(0, 32).Select(value => $"a{(char)value}b").Concat(new[]
        {
            @"a\q", @"a\x20", @"a\u", @"a\u123", @"a\u12xz", @"a\U1234", "a\\",
            "a\" \"b", "a\",\"b", "a\"\nRUN unexpected\n\"b", $"a{escape}{escape}\nb"
        }).ToArray();
        foreach (string operation in WriteOperations)
        {
            foreach (string body in invalid)
            {
                var (owner, _, tokens) = CreateOwner(kind, escape);
                LiteralToken incoming = RawLiteral(body, escape);
                incoming.QuoteChar = '\'';
                string incomingBefore = incoming.ToString();
                Token[] children = incoming.Tokens.ToArray();
                LiteralToken[] original = tokens.ToArray();
                string before = owner.ToString();

                Assert.Throws<InvalidOperationException>(() => Write(tokens, incoming, operation));

                Assert.Equal(before, owner.ToString());
                Assert.Equal(original, tokens);
                Assert.Equal(incomingBefore, incoming.ToString());
                Assert.Equal('\'', incoming.QuoteChar);
                Assert.Equal(children, incoming.Tokens);
            }
        }
    }

    /// <summary>Adopts valid JSON escapes without decoding, re-encoding, or replacing incoming token instances.</summary>
    /// <param name="kind">The JSON collection's owner kind.</param>
    /// <param name="escape">The owner's Dockerfile escape character.</param>
    [Theory]
    [MemberData(nameof(JsonOwners))]
    public void AllTypedWriteRoutesPreserveValidEncodedSyntax(string kind, char escape)
    {
        string[] bodies =
        {
            @"a\\b", @"a\/b", @"a\bb", @"a\fb", @"a\nb", @"a\rb", @"a\tb",
            @"a\u0000b", @"a\u001Fb", @"a\uAbCdb", @"a\uD83D\uDE00b", "a snowman ☃ b"
        };
        if (escape == '\\')
        {
            bodies = bodies.Append("a\\\"b").ToArray();
        }
        foreach (string operation in WriteOperations)
        {
            foreach (string body in bodies)
            {
                var (owner, _, tokens) = CreateOwner(kind, escape);
                LiteralToken incoming = RawLiteral(body, escape);
                Token[] children = incoming.Tokens.ToArray();
                LiteralToken sibling = tokens[1];
                string siblingBefore = sibling.ToString();

                Write(tokens, incoming, operation);

                int index = tokens.IndexOf(incoming);
                Assert.True(index >= 0);
                Assert.Same(incoming, tokens[index]);
                Assert.Equal(children, incoming.Tokens);
                Assert.Equal($"\"{body}\"", incoming.ToString());
                Assert.Equal(JsonSerializer.Deserialize<string>($"\"{body}\""), Decode(owner)[index]);
                Assert.Same(sibling, tokens.Single(token => ReferenceEquals(token, sibling)));
                Assert.Equal(siblingBefore, sibling.ToString());
            }
        }
    }

    /// <summary>Validates physical comments and continuations even when a fresh literal did not tokenize them as trivia.</summary>
    /// <param name="kind">The JSON collection's owner kind.</param>
    /// <param name="escape">The owner's Dockerfile escape character.</param>
    [Theory]
    [MemberData(nameof(JsonOwners))]
    public void FreshContinuedTokensPreservePhysicalSyntaxAndLaterEdits(string kind, char escape)
    {
        foreach (string newline in new[] { "\n", "\r\n" })
        {
            foreach (bool comments in new[] { false, true })
            {
                foreach (string operation in WriteOperations)
                {
                    var (owner, _, tokens) = CreateOwner(kind, escape);
                    string continuation = $"{escape}{newline}" +
                        (comments ? $"# drop{newline}  # drop too{newline}{newline}" : "");
                    LiteralToken incoming = new($"a{continuation}b", escapeChar: escape);
                    string body = incoming.ToString();
                    Token[] children = incoming.Tokens.ToArray();
                    LiteralToken sibling = tokens[1];
                    string siblingText = sibling.ToString();

                    Write(tokens, incoming, operation);

                    Assert.Equal($"\"{body}\"", incoming.ToString());
                    Assert.Equal(children, incoming.Tokens);
                    int index = tokens.IndexOf(incoming);
                    Assert.Equal("ab", Decode(owner, continuation)[index]);
                    foreach (TriviaDisposition trivia in Enum.GetValues<TriviaDisposition>())
                    {
                        string before = owner.ToString();
                        tokens.Replace(index, incoming, trivia);
                        Assert.Equal(before, owner.ToString());
                    }
                    tokens.Move(index, 0);
                    Assert.Same(incoming, tokens[0]);
                    Assert.Equal("ab", Decode(owner, continuation)[0]);
                    Assert.Equal(siblingText, sibling.ToString());
                    tokens.Remove(incoming, TriviaDisposition.Discard);
                    Assert.Contains(sibling, tokens);
                    Assert.Equal(siblingText, sibling.ToString());
                    Decode(owner);
                }
            }
        }
    }

    /// <summary>Checks supported semantic replacements retain selected tokens and do not rewrite escaped siblings.</summary>
    /// <param name="kind">The JSON collection's owner kind.</param>
    /// <param name="escape">The owner's Dockerfile escape character.</param>
    [Theory]
    [MemberData(nameof(JsonOwners))]
    public void SemanticReplacementPreservesIdentityAndEscapedSiblings(string kind, char escape)
    {
        foreach (TriviaDisposition trivia in Enum.GetValues<TriviaDisposition>())
        {
            var (owner, values, tokens) = CreateOwner(kind, escape);
            LiteralToken original = tokens[0];
            LiteralToken sibling = tokens[1];
            string siblingText = sibling.ToString();

            values.Replace(0, "a path with spaces", trivia);

            Assert.Same(original, tokens[0]);
            Assert.Same(sibling, tokens[1]);
            Assert.Equal(siblingText, sibling.ToString());
            Assert.Equal(new[] { "a path with spaces", "sibling\nvalue" }, Decode(owner).Take(2));
        }
    }

    /// <summary>Leaves existing invalid legacy operands outside newcomer validation during self-replacement, moves, and removal.</summary>
    [Fact]
    public void ExistingOperandsAreNotRetroactivelyValidated()
    {
        ExecFormCommand command = ExecFormCommand.Parse("[\"legacy\tvalue\", \"ok\"]");
        LiteralToken existing = command.ValueTokens[0];
        command.ValueTokens.Replace(0, existing);
        command.ValueTokens.Move(0, 1);
        command.Values.Add("new");
        Assert.Same(existing, command.ValueTokens[1]);
        command.ValueTokens.Remove(existing);
        Assert.Equal(new[] { "ok", "new" }, Decode(command));
    }

    /// <summary>Does not impose JSON escaping restrictions on shell-form operands.</summary>
    [Fact]
    public void ShellOperandsKeepExistingBackslashBehavior()
    {
        CopyInstruction copy = CopyInstruction.Parse("COPY old /dest", '`');
        AddInstruction add = AddInstruction.Parse("ADD old /dest", '`');
        VolumeInstruction volume = VolumeInstruction.Parse("VOLUME old", '`');
        foreach (EditableList<string> values in new[] { copy.Sources, add.Sources, volume.Paths })
        {
            values.Add(@"a\q");
            Assert.Equal(@"a\q", values[1]);
        }
    }

    /// <summary>Checks JSON string grammar independently, including complete logical and physical consumption.</summary>
    /// <param name="text">A complete prospective JSON string representation.</param>
    /// <param name="valid">Whether it is exactly one valid JSON string.</param>
    [Theory]
    [InlineData("\"\"", true)]
    [InlineData("\"a\\\"b\"", true)]
    [InlineData("\"\\u0000\\u001F\\uAbCd\"", true)]
    [InlineData("\"a\\\\b\\/c\\b\\f\\n\\r\\t\"", true)]
    [InlineData("\"unterminated", false)]
    [InlineData("\"terminal\\\"", false)]
    [InlineData("\"a\" \"b\"", false)]
    [InlineData("\"a\"\nRUN injected", false)]
    [InlineData("\"a\"\r\n# extra physical input", false)]
    [InlineData(" \"a\"", false)]
    [InlineData("\"a\" ", false)]
    [InlineData("\"a\\u123\"", false)]
    [InlineData("\"a\\u12X4\"", false)]
    [InlineData("\"a\\q\"", false)]
    public void JsonLexicalValidationConsumesOneCompleteString(string text, bool valid)
    {
        foreach (char escape in new[] { '\\', '`' })
        {
            Assert.Equal(valid, JsonStringValidation.IsValid(text, escape));
        }
        if (valid)
        {
            Assert.NotNull(JsonSerializer.Deserialize<string>(text));
        }
        else if (text != " \"a\"" && text != "\"a\" ")
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<string>(text));
        }
    }

    /// <summary>Retains the existing parser limitation for escaped quotes with backtick Dockerfile context.</summary>
    [Fact]
    public void JsonValidationDoesNotBypassExistingOperandGrammar()
    {
        const string body = "a\\\"b";
        Assert.True(JsonStringValidation.IsValid($"\"{body}\"", '`'));
        Assert.Throws<ParseException>(() => ExecFormCommand.Parse($"[\"{body}\"]", '`'));
        ExecFormCommand command = ExecFormCommand.Parse("[\"old\"]", '`');
        LiteralToken incoming = RawLiteral(body, '`');

        Assert.Throws<InvalidOperationException>(() => command.ValueTokens.Add(incoming));

        Assert.Equal("[\"old\"]", command.ToString());
        Assert.Null(incoming.QuoteChar);
    }

    /// <summary>Preserves physical source offsets and heredoc framing when folding a continued JSON header.</summary>
    /// <param name="newline">The physical line ending.</param>
    /// <param name="escape">The Dockerfile continuation character.</param>
    [Theory]
    [InlineData("\n", '\\')]
    [InlineData("\r\n", '\\')]
    [InlineData("\n", '`')]
    [InlineData("\r\n", '`')]
    public void HeaderFoldingRetainsPhysicalBoundariesAndOffsets(string newline, char escape)
    {
        string prefix = $"FROM scratch{newline}";
        string header = $"RUN [\"a{escape} \t{newline} # drop{newline}{newline}b\"]{newline}";
        string text = prefix + header + $"FROM next{newline}";
        List<int> offsets = new();

        string logical = ConstructReader.FoldHeader(text, prefix.Length, escape, out int end, offsets);
        ConstructReader.Region region = ConstructReader.Read(text, prefix.Length, escape);

        Assert.Equal("RUN [\"ab\"]", logical);
        Assert.Equal(prefix.Length + header.Length, end);
        Assert.Equal(end, region.End);
        Assert.Empty(region.Heredocs);
        Assert.Equal(logical.Length, offsets.Count);
        Assert.Equal(logical, new string(offsets.Select(offset => text[offset]).ToArray()));
        Assert.Equal(prefix.Length, offsets[0]);
        Assert.Equal(text.IndexOf("b\"]", StringComparison.Ordinal), offsets[7]);
    }

    private static readonly string[] WriteOperations =
    {
        "add", "insert", "index", "replace-preserve", "replace-discard", "replace-item-preserve",
        "replace-item-discard", "collection-add", "list-insert", "list-index"
    };

    private static void Write<T>(EditableList<T> list, T item, string operation)
    {
        switch (operation)
        {
            case "add": list.Add(item); break;
            case "insert": list.Insert(0, item); break;
            case "index": list[0] = item; break;
            case "replace-preserve": list.Replace(0, item, TriviaDisposition.Preserve); break;
            case "replace-discard": list.Replace(0, item, TriviaDisposition.Discard); break;
            case "replace-item-preserve": list.ReplaceItem(list[0], item, TriviaDisposition.Preserve); break;
            case "replace-item-discard": list.ReplaceItem(list[0], item, TriviaDisposition.Discard); break;
            case "collection-add": ((ICollection<T>)list).Add(item); break;
            case "list-insert": ((IList<T>)list).Insert(0, item); break;
            case "list-index": ((IList<T>)list)[0] = item; break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static LiteralToken RawLiteral(string body, char escape)
    {
        LiteralToken token = new("placeholder", escapeChar: escape);
        Assert.IsType<StringToken>(Assert.Single(token.Tokens)).Value = body;
        return token;
    }

    private static (AggregateToken Owner, EditableList<string> Values, EditableList<LiteralToken> Tokens)
        CreateOwner(string kind, char escape)
    {
        const string operands = "\"old\", \"sibling\\nvalue\"";
        return kind switch
        {
            "exec" => FromExec(ExecFormCommand.Parse($"[{operands}]", escape)),
            "copy" => FromTransfer(CopyInstruction.Parse($"COPY [{operands}, \"/dest\"]", escape)),
            "add" => FromTransfer(AddInstruction.Parse($"ADD [{operands}, \"/dest\"]", escape)),
            "volume" => FromVolume(VolumeInstruction.Parse($"VOLUME [{operands}]", escape)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static (AggregateToken, EditableList<string>, EditableList<LiteralToken>) FromExec(ExecFormCommand owner) =>
        (owner, owner.Values, owner.ValueTokens);

    private static (AggregateToken, EditableList<string>, EditableList<LiteralToken>) FromTransfer(FileTransferInstruction owner) =>
        (owner, owner.Sources, owner.SourceTokens);

    private static (AggregateToken, EditableList<string>, EditableList<LiteralToken>) FromVolume(VolumeInstruction owner) =>
        (owner, owner.Paths, owner.PathTokens);

    private static string[] Decode(AggregateToken owner, string? continuation = null)
    {
        string text = owner.ToString();
        if (continuation is not null)
        {
            text = text.Replace(continuation, "", StringComparison.Ordinal);
        }
        return JsonSerializer.Deserialize<string[]>(text.Substring(text.IndexOf('[')))!;
    }
}
