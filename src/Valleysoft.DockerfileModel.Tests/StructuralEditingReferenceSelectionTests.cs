using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>Verifies structural selection by exact reference for equal-valued built-in tokens.</summary>
public class StructuralEditingReferenceSelectionTests
{
    /// <summary>Operand writes select the exact instance when multiple values have identical text.</summary>
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
        ExecFormCommand owner = ExecFormCommand.Parse("[\"same\", \"same\"]");
        LiteralToken first = owner.ValueTokens[0];
        LiteralToken second = owner.ValueTokens[1];
        LiteralToken incoming = new("same");
        Assert.Equal(1, owner.ValueTokens.IndexOf(second));
        Assert.DoesNotContain(incoming, owner.ValueTokens);
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
    }

    /// <summary>Comment writes preserve exact identities when comments have equal text.</summary>
    /// <param name="operation">The structural edit applied to the second comment or its adjacent boundary.</param>
    [Theory]
    [InlineData("add")]
    [InlineData("insert")]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("move")]
    public void EqualCommentsRetainExactSelection(string operation)
    {
        Instruction owner = CommentOwner(2);
        CommentToken first = owner.CommentTokens[0];
        CommentToken second = owner.CommentTokens[1];
        CommentToken incoming = CommentToken.Parse("#same\n");
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
            ? CommentOwner(2)
            : ExposeInstruction.Parse("EXPOSE 80 80");
        Token foreign = commentAnchor ? CommentToken.Parse("#same\n") : new LiteralToken("80");
        string before = owner.ToString();
        Token[] roots = owner.Tokens.ToArray();
        CommentToken[] comments = owner.CommentTokens.ToArray();

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
    }

    /// <summary>Legacy ENV conversion locates a value after its actual key.</summary>
    [Fact]
    public void LegacyEnvConversionUsesReferencePositions()
    {
        EnvInstruction owner = EnvInstruction.Parse("ENV same same");
        KeyValueToken<Variable, LiteralToken> pair = owner.VariableTokens[0];
        Variable key = pair.KeyToken;
        LiteralToken value = pair.ValueToken!;

        owner.VariableTokens.Add(new KeyValueToken<Variable, LiteralToken>(new Variable("NEXT"), new LiteralToken("value")));

        Assert.Same(pair, owner.VariableTokens[0]);
        Assert.Same(key, pair.KeyToken);
        Assert.Same(value, pair.ValueToken);
        Assert.Equal("ENV same=same NEXT=value", owner.ToString());
    }

    /// <summary>Semantic comment replacement changes its payload while preserving the marker token identity.</summary>
    [Fact]
    public void CommentPayloadReplacementUsesReferencePosition()
    {
        Instruction owner = CommentOwner(1);
        CommentToken comment = owner.CommentTokens.Single();
        Token marker = comment.Tokens.First();

        owner.Comments[0] = "updated";

        Assert.Same(comment, owner.CommentTokens.Single());
        Assert.Same(marker, comment.Tokens.First());
        Assert.Equal("updated", comment.Text);
        Assert.Equal("RUN \\\n#updated\necho hi", owner.ToString());
    }

    /// <summary>Creates a built-in RUN instruction with the requested number of comments.</summary>
    private static Instruction CommentOwner(int count) =>
        RunInstruction.Parse($"RUN \\\n{String.Concat(Enumerable.Repeat("#same\n", count))}echo hi");

    /// <summary>Compares identities explicitly rather than relying on value equality.</summary>
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
}
