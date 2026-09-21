using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>Verifies structural selection by exact reference even when supported consumer tokens override value equality.</summary>
public class StructuralEditingReferenceSelectionTests
{
    /// <summary>Operand writes select the exact instance without invoking consumer equality, even for a correct-position match.</summary>
    /// <param name="operation">The structural operation targeting the second equal-valued operand.</param>
    [Theory]
    [InlineData("insert")]
    [InlineData("replace")]
    [InlineData("replace-item")]
    [InlineData("remove-at")]
    [InlineData("remove")]
    [InlineData("move")]
    public void EqualOperandsRetainExactSelection(string operation)
    {
        EqualLiteral first = new("same");
        EqualLiteral second = new("same");
        EqualLiteral incoming = new("same");
        SeededCommand owner = new(first, second);
        Assert.Equal(1, owner.ValueTokens.IndexOf(second));
        bool containsIncoming = owner.ValueTokens.Contains(incoming);
        Assert.False(containsIncoming);
        LiteralToken[] expected;

        switch (operation)
        {
            case "insert":
                owner.ValueTokens.Insert(1, incoming);
                expected = new[] { first, incoming, second };
                break;
            case "replace":
                owner.ValueTokens[1] = incoming;
                expected = new[] { first, incoming };
                break;
            case "replace-item":
                owner.ValueTokens.ReplaceItem(second, incoming);
                expected = new[] { first, incoming };
                break;
            case "remove-at":
                owner.ValueTokens.RemoveAt(1);
                expected = new[] { first };
                break;
            case "remove":
                Assert.True(owner.ValueTokens.Remove(second));
                expected = new[] { first };
                break;
            case "move":
                owner.ValueTokens.Move(1, 0);
                expected = new[] { second, first };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }

        AssertReferences(expected, owner.ValueTokens);
        Assert.Equal(owner.Values, ExecFormCommand.Parse(owner.ToString()).Values);
        Assert.Equal(0, first.EqualityCalls);
        Assert.Equal(0, second.EqualityCalls);
        Assert.Equal(0, incoming.EqualityCalls);
    }

    /// <summary>Comment writes preserve exact identities without invoking their consumer equality implementations.</summary>
    /// <param name="operation">The structural edit applied to the second comment or its adjacent boundary.</param>
    [Theory]
    [InlineData("add")]
    [InlineData("insert")]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("move")]
    public void EqualCommentsRetainExactSelection(string operation)
    {
        EqualComment first = new();
        EqualComment second = new();
        EqualComment incoming = new();
        Instruction owner = CommentOwner(first, second);
        CommentToken[] expected;

        switch (operation)
        {
            case "add":
                owner.CommentTokens.Add(incoming);
                expected = new[] { first, second, incoming };
                break;
            case "insert":
                owner.CommentTokens.Insert(1, incoming);
                expected = new[] { first, incoming, second };
                break;
            case "replace":
                owner.CommentTokens[1] = incoming;
                expected = new[] { first, incoming };
                break;
            case "remove":
                owner.CommentTokens.RemoveAt(1);
                expected = new[] { first };
                break;
            case "move":
                owner.CommentTokens.Move(1, 0);
                expected = new[] { second, first };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }

        AssertReferences(expected, owner.CommentTokens);
        Assert.Equal(expected.Length, Dockerfile.Parse(owner.ToString()).Items.OfType<Instruction>().Single().Comments.Count);
        Assert.Equal(0, first.EqualityCalls);
        Assert.Equal(0, second.EqualityCalls);
        Assert.Equal(0, incoming.EqualityCalls);
    }

    /// <summary>An equal-valued foreign comment or semantic anchor never acquires owner membership.</summary>
    /// <param name="commentAnchor">Whether the foreign token is a comment rather than a direct instruction operand.</param>
    /// <param name="after">Whether insertion targets the boundary after the foreign anchor.</param>
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ForeignEqualAnchorsRejectWithoutMutation(bool commentAnchor, bool after)
    {
        Instruction owner = commentAnchor
            ? CommentOwner(new EqualComment(), new EqualComment())
            : new SeededInstruction(new Token[]
            {
                new KeywordToken("EXPOSE"), new WhitespaceToken(" "), new EqualLiteral("80"),
                new WhitespaceToken(" "), new EqualLiteral("80")
            });
        Token foreign = commentAnchor ? new EqualComment() : new EqualLiteral("80");
        string before = owner.ToString();
        Token[] roots = owner.Tokens.ToArray();
        CommentToken[] comments = owner.CommentTokens.ToArray();
        Token[] foreignChildren = ((AggregateToken)foreign).Tokens.ToArray();

        Assert.Throws<ArgumentException>(() =>
        {
            if (after)
            {
                owner.InsertCommentAfter(foreign, "foreign");
            }
            else
            {
                owner.InsertCommentBefore(foreign, "foreign");
            }
        });

        Assert.Equal(before, owner.ToString());
        AssertReferences(roots, owner.Tokens);
        AssertReferences(comments, owner.CommentTokens);
        AssertReferences(foreignChildren, ((AggregateToken)foreign).Tokens);
        Assert.All(roots.Append(foreign).OfType<EqualLiteral>(), token => Assert.Equal(0, token.EqualityCalls));
        Assert.All(roots.Append(foreign).OfType<EqualComment>(), token => Assert.Equal(0, token.EqualityCalls));
    }

    /// <summary>Legacy ENV conversion locates the value after its actual key even when that key compares equal to the value.</summary>
    [Fact]
    public void LegacyEnvConversionUsesReferencePositions()
    {
        EqualVariable key = new("same");
        LiteralToken value = new("same");
        KeyValueToken<Variable, LiteralToken> pair = new(key, value, separator: ' ');
        SeededEnv owner = new(pair);

        owner.VariableTokens.Add(new KeyValueToken<Variable, LiteralToken>(new Variable("NEXT"), new LiteralToken("value")));

        Assert.Same(pair, owner.VariableTokens[0]);
        Assert.Same(key, pair.KeyToken);
        Assert.Same(value, pair.ValueToken);
        Assert.Equal("ENV same=same NEXT=value", owner.ToString());
    }

    /// <summary>Semantic comment replacement changes its payload rather than an equal-valued marker token.</summary>
    [Fact]
    public void CommentPayloadReplacementUsesReferencePosition()
    {
        PayloadComment comment = new();
        Token marker = comment.Tokens.First();
        Instruction owner = CommentOwner(comment);

        owner.Comments[0] = "updated";

        Assert.Same(comment, owner.CommentTokens.Single());
        Assert.Same(marker, comment.Tokens.First());
        Assert.Equal("updated", comment.Text);
        Assert.Equal("RUN \\\n#updated\necho hi", owner.ToString());
    }

    /// <summary>Creates a valid instruction with known comment identities without exercising the writes under test.</summary>
    /// <param name="comments">The already newline-terminated comments to retain as direct children.</param>
    /// <returns>An external instruction using inherited supported serialization.</returns>
    private static Instruction CommentOwner(params CommentToken[] comments) =>
        new SeededInstruction(new Token[] { new KeywordToken("RUN"), new WhitespaceToken(" "), new LineContinuationToken("\n") }
            .Concat(comments).Append(new LiteralToken("echo hi")));

    /// <summary>Compares identities explicitly so custom Equals cannot mask an incorrect test result.</summary>
    /// <typeparam name="T">The token or model reference type.</typeparam>
    /// <param name="expected">The exact references in their required order.</param>
    /// <param name="actual">The current view to inspect.</param>
    private static void AssertReferences<T>(IReadOnlyList<T> expected, IEnumerable<T> actual) where T : class
    {
        T[] snapshot = actual.ToArray();
        Assert.Equal(expected.Count, snapshot.Length);
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.Same(expected[index], snapshot[index]);
        }
    }

    /// <summary>Uses ordinary consumer equality without changing serialization or quote implementation.</summary>
    /// <param name="value">The value shared by otherwise distinct operands.</param>
    private sealed class EqualLiteral(string value) : LiteralToken(value)
    {
        /// <summary>Detects default-equality searches even when they happen to find the intended instance.</summary>
        public int EqualityCalls { get; private set; }

        /// <summary>Compares equal-text consumer operands by value.</summary>
        /// <param name="obj">The object being compared.</param>
        /// <returns>True for an equal-valued consumer literal.</returns>
        public override bool Equals(object? obj)
        {
            EqualityCalls++;
            return obj is EqualLiteral other && Value == other.Value;
        }

        /// <summary>Matches the intentionally colliding consumer equality policy.</summary>
        /// <returns>A stable shared hash code.</returns>
        public override int GetHashCode() => 0;
    }

    /// <summary>Installs equal-valued operand objects through protected storage without exercising insertion.</summary>
    private sealed class SeededCommand : ExecFormCommand
    {
        /// <summary>Retains both distinct literals in known JSON positions.</summary>
        /// <param name="first">The first operand.</param>
        /// <param name="second">The second, equal-valued operand.</param>
        public SeededCommand(LiteralToken first, LiteralToken second) : base(new[] { "same", "same" })
        {
            first.QuoteChar = second.QuoteChar = '"';
            int[] positions = TokenList.Select((token, index) => (token, index)).Where(item => item.token is LiteralToken)
                .Select(item => item.index).ToArray();
            TokenList[positions[0]] = first;
            TokenList[positions[1]] = second;
        }
    }

    /// <summary>Provides an external instruction using the built-in serializer and explicitly supplied child identities.</summary>
    /// <param name="tokens">The valid instruction token sequence.</param>
    private sealed class SeededInstruction(IEnumerable<Token> tokens) : Instruction(tokens);

    /// <summary>Compares otherwise distinct comment objects as equal without changing serialization.</summary>
    private sealed class EqualComment : CommentToken
    {
        /// <summary>Detects unwanted equality dispatch during comment selection or anchor validation.</summary>
        public int EqualityCalls { get; private set; }

        /// <summary>Creates a complete continued comment line.</summary>
        public EqualComment() : base("same") => TokenList.Add(new NewLineToken("\n"));

        /// <summary>Applies the consumer's equal-value policy to comment instances.</summary>
        /// <param name="obj">The object being compared.</param>
        /// <returns>True for another equal-comment instance.</returns>
        public override bool Equals(object? obj)
        {
            EqualityCalls++;
            return obj is EqualComment;
        }

        /// <summary>Uses a hash compatible with the consumer equality policy.</summary>
        /// <returns>A stable shared hash code.</returns>
        public override int GetHashCode() => 0;
    }

    /// <summary>Allows equal text across key and value token types to exercise nested list positioning.</summary>
    /// <param name="name">The variable name.</param>
    private sealed class EqualVariable(string name) : Variable(name)
    {
        /// <summary>Compares any value token with the same semantic value as equal.</summary>
        /// <param name="obj">The potentially equal value token.</param>
        /// <returns>True for an equal semantic value.</returns>
        public override bool Equals(object? obj) => obj is IValueToken other && Value == other.Value;

        /// <summary>Uses a shared hash for the deliberately broad equality policy.</summary>
        /// <returns>A stable hash code.</returns>
        public override int GetHashCode() => 0;
    }

    /// <summary>Seeds legacy ENV syntax while preserving a consumer-defined key instance.</summary>
    private sealed class SeededEnv : EnvInstruction
    {
        /// <summary>Installs the legacy assignment through protected storage.</summary>
        /// <param name="pair">The assignment whose key and value compare equal.</param>
        public SeededEnv(KeyValueToken<Variable, LiteralToken> pair) : base(new Dictionary<string, string> { ["same"] = "same" }) =>
            TokenList[TokenList.FindIndex(token => token is KeyValueToken<Variable, LiteralToken>)] = pair;
    }

    /// <summary>Creates an equal-valued marker and payload while retaining ordinary comment serialization.</summary>
    private sealed class PayloadComment : CommentToken
    {
        /// <summary>Seeds the marker before its equal-text payload.</summary>
        public PayloadComment() : base("#")
        {
            TokenList[0] = new EqualSymbol();
            TokenList.Add(new NewLineToken("\n"));
        }
    }

    /// <summary>Compares its hash marker with an equal-text string payload.</summary>
    private sealed class EqualSymbol() : SymbolToken('#')
    {
        /// <summary>Applies semantic equality across primitive token types.</summary>
        /// <param name="obj">The object being compared with the marker.</param>
        /// <returns>True for another hash-valued primitive.</returns>
        public override bool Equals(object? obj) => obj is PrimitiveToken other && Value == other.Value;

        /// <summary>Matches the broad primitive equality policy.</summary>
        /// <returns>A stable shared hash code.</returns>
        public override int GetHashCode() => 0;
    }
}
