using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class ArgResolutionGuardTests
{
    public static IEnumerable<object[]> SourceEdits =>
        from stageScope in new[] { false, true }
        from updateInline in new[] { false, true }
        from targetOnly in new[] { false, true }
        from edit in new[]
        {
            "primitive", "quote", "value", "children", "rawChildren", "name", "document", "builder"
        }
        select new object[] { stageScope, updateInline, targetOnly, edit };

    [Theory]
    [MemberData(nameof(SourceEdits))]
    public void DocumentResolutionRejectsCallbackSourceEdits(bool stageScope, bool updateInline,
        bool targetOnly, string edit)
    {
        Dockerfile document = Dockerfile.Parse(stageScope
            ? "FROM alpine\nARG X=placeholder\nENV RESULT=$X"
            : "ARG X=placeholder\nFROM prefix$X");
        ArgDeclaration declaration = document.Items.OfType<ArgInstruction>().Single().ArgTokens[0];
        StringToken primitive = new("before");
        CallbackVariable callback = new();
        Token[] tokens = { primitive, callback };
        LiteralToken value = edit == "rawChildren"
            ? new RawLiteral(tokens) : new LiteralToken(tokens, true, '\\');
        declaration.ValueToken = value;
        callback.Action = edit switch
        {
            "primitive" => () => primitive.Value = "after",
            "quote" => () => value.QuoteChar = '\'',
            "value" => () => value.Value = value.Value,
            "children" => () => ((IList<Token>)value.Tokens).Add(new StringToken("after")),
            "rawChildren" => () => ((RawLiteral)value).Append(),
            "name" => () => declaration.Name = "CHANGED",
            "document" => () => document.Items.Add(new ArgInstruction("ADDED", "after")),
            "builder" => () => new DockerfileBuilder(document).ArgInstruction("ADDED", "after"),
            _ => throw new ArgumentOutOfRangeException(nameof(edit))
        };
        string original = document.ToString();

        ResolutionOptions options = new() { UpdateInline = updateInline };
        Assert.Throws<InvalidOperationException>(() => targetOnly
            ? document.ResolveVariables(document.Items.OfType<Instruction>().Last(), options: options)
            : document.ResolveVariables(options: options));

        if (edit != "rawChildren")
        {
            Assert.Equal(original, document.ToString());
        }
        callback.Action = null;
        value.Value = "recovered";
        Assert.Equal(stageScope ? "ENV RESULT=recovered" : "FROM prefixrecovered",
            document.ResolveVariables());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void DocumentResolutionAllowsDictionaryAndOptionsChanges(bool stageScope, bool updateInline, bool worker)
    {
        Dockerfile document = Dockerfile.Parse(stageScope
            ? "FROM alpine\nARG X=placeholder\nENV RESULT=$X"
            : "ARG X=placeholder\nFROM prefix$X");
        CallbackVariable callback = new()
        {
            Resolve = (variables, options) =>
            {
                void Change()
                {
                    variables["PENDING"] = "changed";
                    options.RemoveEscapeCharacters = false;
                }
                if (worker) Task.Run(Change).GetAwaiter().GetResult();
                else Change();
                return "callback";
            }
        };
        document.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken =
            new LiteralToken(new Token[] { new VariableRefToken("PENDING"), callback }, true, '\\');
        string original = document.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=changedcallback" : "FROM prefixchangedcallback",
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        if (!updateInline)
        {
            Assert.Equal(original, document.ToString());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OverrideLookupRejectsSourceEdits(bool stageScope, bool customComparer)
    {
        Dockerfile document = Dockerfile.Parse(stageScope
            ? "FROM alpine\nARG X=before\nENV RESULT=$X"
            : "ARG X=before\nFROM prefix$X");
        LiteralToken value = document.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken!;
        CallbackComparer comparer = new();
        CallbackDictionary dictionary = new();
        IDictionary<string, string?> overrides = customComparer
            ? new Dictionary<string, string?>(comparer) { ["X"] = "override" }
            : dictionary;
        Action edit = () => value.Value = "after";
        comparer.Action = dictionary.Action = edit;
        string original = document.ToString();

        Assert.Throws<InvalidOperationException>(() => document.ResolveVariables(overrides));
        Assert.Equal(original, document.ToString());
        comparer.Action = dictionary.Action = null;
        Assert.Equal(stageScope ? "ENV RESULT=before" : "FROM prefixbefore", document.ResolveVariables());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneResolutionProtectsCompletedModifierSubtrees(bool updateInline)
    {
        LiteralToken operand = new("old", canContainVariables: true);
        VariableRefToken right = new("UNSET", ":-", "placeholder") { ModifierValueToken = operand };
        CallbackVariable left = new() { Action = () => operand.Value = "changed" };
        LiteralToken root = new(new Token[] { left, right }, true, '\\');

        Assert.Throws<InvalidOperationException>(() =>
            root.ResolveVariables('\\', options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("old", operand.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneVariableLookupRejectsSourceEdits(bool updateInline)
    {
        VariableRefToken source = new("X");
        CallbackDictionary variables = new() { Action = () => source.VariableName = "CHANGED" };

        Assert.Throws<InvalidOperationException>(() =>
            source.ResolveVariables('\\', variables, new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("X", source.VariableName);
        variables.Action = null;
        variables["X"] = "recovered";
        Assert.Equal("recovered", source.ResolveVariables('\\', variables));
    }

    [Theory]
    [InlineData(false, 2000)]
    [InlineData(false, 8000)]
    [InlineData(true, 2000)]
    [InlineData(true, 8000)]
    public void LargeArgOptionsCallbackWorkloadsPreserveSource(bool stageScope, int count)
    {
        OptionsLookupAggregate aggregate = new(count);
        Dockerfile document = Dockerfile.Parse(stageScope
            ? "FROM alpine\nARG X=placeholder\nENV RESULT=$X"
            : "ARG X=placeholder\nFROM prefix$X");
        document.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken =
            new LiteralToken(new[] { aggregate }, true, '\\');
        string original = document.ToString();

        Assert.Equal((stageScope ? "ENV RESULT=" : "FROM prefix") +
            String.Concat(Enumerable.Repeat(".x", count)) + "suffix", document.ResolveVariables());
        Assert.Equal(count, aggregate.LookupCount);
        Assert.Equal(original, document.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeLiteralWrappersPreserveCallbackOptionsAndInlineDecisions(bool stageScope, bool updateInline)
    {
        const int count = 512;
        int calls = 0;
        Dockerfile document = Dockerfile.Parse(stageScope
            ? "FROM alpine\nARG X=placeholder\nENV RESULT=$X"
            : "ARG X=placeholder\nFROM prefix$X");
        LiteralToken value = new(Enumerable.Range(0, count).SelectMany(_ => new Token[]
        {
            new LiteralToken(new[] { new LiteralToken(".") }, true, '\\'),
            new CallbackVariable
            {
                Resolve = (_, options) =>
                {
                    options.RemoveEscapeCharacters = ++calls % 2 == 0;
                    return "x";
                }
            }
        }), true, '\\');
        document.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken = value;
        string original = document.ToString();

        Assert.Equal((stageScope ? "ENV RESULT=" : "FROM prefix") +
            String.Concat(Enumerable.Repeat(".x", count)),
            document.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(count, calls);
        if (!updateInline) Assert.Equal(original, document.ToString());
    }

    private sealed class OptionsLookupAggregate(int count) : AggregateToken(
        Enumerable.Range(0, count).SelectMany(_ => new Token[]
        {
            new StringToken("."), new VariableRefToken("INNER")
        }).Append(new StringToken("suffix")))
    {
        public int LookupCount { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            CallbackDictionary lookups = new()
            {
                ["INNER"] = "x",
                Action = () => options!.RemoveEscapeCharacters = ++LookupCount % 2 == 0
            };
            return base.ResolveVariables(escapeChar, lookups, options);
        }
    }

    private sealed class CallbackDictionary : Dictionary<string, string?>, IDictionary<string, string?>
    {
        public Action? Action { get; set; }

        public new bool TryGetValue(string key, out string? value)
        {
            Action?.Invoke();
            return base.TryGetValue(key, out value);
        }
    }

    private sealed class RawLiteral : LiteralToken
    {
        public RawLiteral(IEnumerable<Token> tokens) : base(tokens, true, '\\') { }

        public void Append() => TokenList.Add(new StringToken("after"));
    }

    private sealed class CallbackComparer : IEqualityComparer<string>
    {
        public Action? Action { get; set; }

        public bool Equals(string? left, string? right) => StringComparer.Ordinal.Equals(left, right);

        public int GetHashCode(string value)
        {
            Action?.Invoke();
            return StringComparer.Ordinal.GetHashCode(value);
        }
    }

    private sealed class CallbackVariable : VariableRefToken
    {
        public CallbackVariable() : base("CALLBACK") { }

        public Action? Action { get; set; }
        public Func<IDictionary<string, string?>, ResolutionOptions, string?>? Resolve { get; init; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            Action?.Invoke();
            return Resolve is null ? "callback" : Resolve(variables!, options!);
        }
    }
}
