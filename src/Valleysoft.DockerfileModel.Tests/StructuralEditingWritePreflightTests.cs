using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>Guards write admission before facade bookkeeping can serialize an unsupported existing token.</summary>
public class StructuralEditingWritePreflightTests
{
    /// <summary>Every concrete and interface write rejects unsupported owners before projecting values or publishing objects.</summary>
    /// <param name="operation">The facade or compatibility-interface entry point being exercised.</param>
    [Theory]
    [InlineData("add")]
    [InlineData("insert")]
    [InlineData("indexer")]
    [InlineData("replace")]
    [InlineData("replace-item")]
    [InlineData("remove")]
    [InlineData("remove-at")]
    [InlineData("clear")]
    [InlineData("clear-discard")]
    [InlineData("move")]
    [InlineData("move-self")]
    [InlineData("remove-missing")]
    [InlineData("replace-missing")]
    [InlineData("insert-invalid-index")]
    [InlineData("interface-add")]
    [InlineData("interface-insert")]
    [InlineData("interface-indexer")]
    [InlineData("interface-remove")]
    [InlineData("interface-remove-at")]
    [InlineData("interface-clear")]
    [InlineData("typed-add")]
    [InlineData("typed-replace")]
    public void WritesPreflightBeforeProjectedBookkeeping(string operation)
    {
        MutatingLiteral existing = new();
        SeededCommand owner = new(existing);
        LiteralToken incoming = new("incoming");
        Token[] roots = owner.Tokens.ToArray();
        Token[] children = existing.Tokens.ToArray();
        Token[] incomingChildren = incoming.Tokens.ToArray();
        LiteralToken[] values = owner.ValueTokens.ToArray();
        IList<string> compatibility = owner.Values;

        Assert.Throws<InvalidOperationException>(() =>
        {
            switch (operation)
            {
                case "add": owner.Values.Add("incoming"); break;
                case "insert": owner.Values.Insert(1, "incoming"); break;
                case "indexer": owner.Values[0] = "incoming"; break;
                case "replace": owner.Values.Replace(0, "incoming"); break;
                case "replace-item": owner.Values.ReplaceItem("original", "incoming"); break;
                case "remove": owner.Values.Remove("original"); break;
                case "remove-at": owner.Values.RemoveAt(0); break;
                case "clear": owner.Values.Clear(); break;
                case "clear-discard": owner.Values.Clear(TriviaDisposition.Discard); break;
                case "move": owner.Values.Move(0, 1); break;
                case "move-self": owner.Values.Move(0, 0); break;
                case "remove-missing": owner.Values.Remove("missing"); break;
                case "replace-missing": owner.Values.ReplaceItem("missing", "incoming"); break;
                case "insert-invalid-index": owner.Values.Insert(-1, "incoming"); break;
                case "interface-add": compatibility.Add("incoming"); break;
                case "interface-insert": compatibility.Insert(1, "incoming"); break;
                case "interface-indexer": compatibility[0] = "incoming"; break;
                case "interface-remove": compatibility.Remove("original"); break;
                case "interface-remove-at": compatibility.RemoveAt(0); break;
                case "interface-clear": compatibility.Clear(); break;
                case "typed-add": owner.ValueTokens.Add(incoming); break;
                case "typed-replace": owner.ValueTokens[1] = incoming; break;
            }
        });

        Assert.Equal(0, existing.Calls);
        Assert.Equal("original", Assert.IsType<StringToken>(Assert.Single(existing.Tokens)).Value);
        Assert.Equal(roots.Length, owner.Tokens.Count());
        for (int index = 0; index < roots.Length; index++)
        {
            Assert.Same(roots[index], owner.Tokens.ElementAt(index));
        }
        Assert.Equal(children, existing.Tokens);
        Assert.Equal(values, owner.ValueTokens);
        Assert.Equal(incomingChildren, incoming.Tokens);
        Assert.Null(incoming.QuoteChar);
        Assert.Equal(-1, owner.ValueTokens.IndexOf(incoming));
    }

    /// <summary>Ordinary reads still use consumer serialization rather than imposing the structural write boundary.</summary>
    [Fact]
    public void ReadOnlyProjectionDoesNotRequireWriteEligibility()
    {
        CountingLiteral existing = new();
        SeededCommand owner = new(existing);
        IReadOnlyList<string> readOnly = owner.Values;

        Assert.Equal(2, readOnly.Count);
        Assert.Equal("original", readOnly[0]);
        bool containsOriginal = owner.Values.Contains("original");
        Assert.True(containsOriginal);
        Assert.Equal(0, owner.Values.IndexOf("original"));
        string[] values = new string[2];
        owner.Values.CopyTo(values, 0);
        Assert.Equal(new[] { "original", "other" }, values);
        Assert.Equal(values, owner.Values.ToArray());
        Assert.True(existing.Calls > 0);
        int calls = existing.Calls;

        Assert.Throws<InvalidOperationException>(() => owner.Values.Add("incoming"));

        Assert.Equal(calls, existing.Calls);
        Assert.Same(existing, owner.ValueTokens[0]);
    }

    /// <summary>Seeds an external literal through the preexisting protected-list extension surface without serializing it.</summary>
    private sealed class SeededCommand : ExecFormCommand
    {
        /// <summary>Installs a live operand before structural write admission is exercised.</summary>
        /// <param name="existing">The consumer literal whose serializer must not run during rejected writes.</param>
        public SeededCommand(LiteralToken existing) : base(new[] { "placeholder", "other" })
        {
            TokenList[TokenList.FindIndex(token => token is LiteralToken)] = existing;
        }
    }

    /// <summary>Simulates an ordinary consumer exception with an irreversible mutation before the throw.</summary>
    private sealed class MutatingLiteral() : LiteralToken("original")
    {
        /// <summary>Counts calls that must remain zero throughout rejected write preparation.</summary>
        public int Calls { get; private set; }

        /// <summary>Mutates a live child before throwing, exposing unsafe eager projection.</summary>
        /// <param name="options">The options passed by the semantic value getter.</param>
        /// <returns>No value; this consumer implementation always throws.</returns>
        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            Calls++;
            ((StringToken)Tokens.Single()).Value = "mutated";
            throw new InvalidOperationException("Consumer serializer ran before write admission.");
        }
    }

    /// <summary>Supplies a readable consumer implementation that is nevertheless outside the write contract.</summary>
    private sealed class CountingLiteral() : LiteralToken("original")
    {
        /// <summary>Counts permitted read calls and detects unintended write-time calls.</summary>
        public int Calls { get; private set; }

        /// <summary>Forwards read serialization while remaining a custom effective override.</summary>
        /// <param name="options">The caller's serialization options.</param>
        /// <returns>The original literal value.</returns>
        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            Calls++;
            return base.GetUnderlyingValue(options);
        }
    }
}
