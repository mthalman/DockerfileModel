using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class TypedParserDirectiveTests
{
    [Theory]
    [InlineData("#syntax=docker/dockerfile:1", typeof(SyntaxDirective))]
    [InlineData("# EsCaPe = `", typeof(EscapeDirective))]
    [InlineData("#CHECK=error=true", typeof(CheckDirective))]
    [InlineData("#custom=value", typeof(ParserDirective))]
    public void GenericParsingProducesTypedKnownDirectives(string text, Type type)
    {
        ParserDirective directive = ParserDirective.Parse(text);
        Assert.Equal(type, directive.GetType());
        Assert.Equal(text, directive.ToString());
    }

    [Fact]
    public void TypedParsersRejectOtherNamesAndMultipleLines()
    {
        Assert.Throws<ParseException>(() => SyntaxDirective.Parse("#escape=\\"));
        Assert.Throws<ParseException>(() => EscapeDirective.Parse("#syntax=abc"));
        Assert.Throws<ParseException>(() => CheckDirective.Parse("#custom=abc"));
        Assert.Throws<ParseException>(() => ParserDirective.Parse("#syntax=abc\n#check=skip=all"));
        Assert.Throws<ParseException>(() => ParserDirective.Parse("#syntax="));
    }

    [Fact]
    public void RawValueEditsDoNotInterpretQuotesVariablesOrEscapes()
    {
        SyntaxDirective directive = SyntaxDirective.Parse(" # SyNtAx = original  \r\n");
        directive.DirectiveValue = "\"a $VALUE \\\\ b\"";
        Assert.Equal("\"a $VALUE \\\\ b\"", directive.DirectiveValue);
        Assert.Equal(" # SyNtAx = \"a $VALUE \\\\ b\"  \r\n", directive.ToString());
        directive.DirectiveValueToken.Value = "'second $VALUE'";
        Assert.Equal(" # SyNtAx = 'second $VALUE'  \r\n", directive.ToString());
        directive.DirectiveValueToken = new LiteralToken("\"docker/dockerfile:1\"");
        Assert.Equal("\"docker/dockerfile:1\"", directive.DirectiveValue);
        Assert.Equal(DockerfileFrontendKind.Unresolved, directive.Frontend.Kind);
        directive.DirectiveValue = "\"new literal\"";
        Assert.Equal(" # SyNtAx = \"new literal\"  \r\n", directive.ToString());
        Assert.Single(directive.DirectiveNameToken.Tokens.OfType<StringToken>()).Value = "check";
        Assert.Throws<InvalidOperationException>(() => directive.Frontend);
    }

    [Fact]
    public void EscapeAccessorReflectsEditsAndRejectsInvalidValues()
    {
        EscapeDirective directive = new('`');
        Assert.Equal('`', directive.EscapeChar);
        directive.EscapeChar = '\\';
        Assert.Equal("#escape=\\", directive.ToString());
        directive.DirectiveValue = "bad";
        Assert.Throws<InvalidOperationException>(() => directive.EscapeChar);
        Assert.Throws<ArgumentOutOfRangeException>(() => new EscapeDirective('x'));
        Assert.Single(directive.DirectiveNameToken.Tokens.OfType<StringToken>()).Value = "syntax";
        Assert.Throws<InvalidOperationException>(() => directive.EscapeChar = '`');
    }

    [Fact]
    public void CheckOptionsAreLiveImmutableSnapshots()
    {
        CheckDirective directive = CheckDirective.Parse(
            "# ChEcK = skip=JSONArgsRecommended,FutureCheck;experimental=all;error=true \r\n");
        string original = directive.ToString();
        Assert.True(directive.TryGetOptions(out CheckDirectiveOptions? first, out string? error), error);
        Assert.Equal(new[] { "JSONArgsRecommended", "FutureCheck" }, first!.SkippedChecks);
        Assert.True(first.ExperimentalAll);
        Assert.True(first.WarningsAsErrors);
        Assert.False(first.SkipAll);
        Assert.Equal(original, directive.ToString());

        directive.DirectiveValueToken.Value = "skip=all;error=false";
        Assert.True(directive.TryGetOptions(out CheckDirectiveOptions? second, out error), error);
        Assert.True(second!.SkipAll);
        Assert.False(second.WarningsAsErrors);
        Assert.True(first.WarningsAsErrors);
        Assert.Single(directive.DirectiveNameToken.Tokens.OfType<StringToken>()).Value = "syntax";
        Assert.False(directive.TryGetOptions(out second, out error));
        Assert.Null(second);
        Assert.NotEmpty(error!);
    }

    [Fact]
    public void EmptyCheckTokenEditIsNotProjectedAsValidDefaults()
    {
        CheckDirective directive = new("skip=all");
        directive.DirectiveValueToken.Value = "";
        Assert.False(directive.TryGetOptions(out CheckDirectiveOptions? options, out string? error));
        Assert.Null(options);
        Assert.NotEmpty(error!);
    }

    [Theory]
    [InlineData("error=1", true)]
    [InlineData("error=True", true)]
    [InlineData("error=TRUE", true)]
    [InlineData("error=t", true)]
    [InlineData("error=F", false)]
    [InlineData("error=false", false)]
    [InlineData("error=true;error=false", false)]
    [InlineData("error=\ttrue", true)]
    public void CheckBooleanGrammarMatchesBuildKit(string value, bool expected)
    {
        CheckDirective directive = new(value);
        Assert.True(directive.TryGetOptions(out CheckDirectiveOptions? options, out string? error), error);
        Assert.Equal(expected, options!.WarningsAsErrors);
    }

    [Theory]
    [InlineData("skip", "all", "Foo", "Foo", true)]
    [InlineData("skip", "Foo", "all", "Foo", true)]
    [InlineData("skip", "Foo", "Bar", "Bar", false)]
    [InlineData("experimental", "all", "Foo", "Foo", true)]
    [InlineData("experimental", "Foo", "all", "Foo", true)]
    [InlineData("experimental", "Foo", "Bar", "Bar", false)]
    public void RepeatedCheckOptionsPreserveBuildKitAllFlagsAndLastList(
        string key, string first, string second, string expectedName, bool expectedAll)
    {
        string text = $"#check={key}={first};{key}={second}";
        CheckDirective directive = CheckDirective.Parse(text);

        Assert.True(directive.TryGetOptions(out CheckDirectiveOptions? options, out string? error), error);
        Assert.Equal(expectedAll, key == "skip" ? options!.SkipAll : options!.ExperimentalAll);
        Assert.Equal(new[] { expectedName }, key == "skip" ? options.SkippedChecks : options.ExperimentalChecks);
        Assert.Equal(text, directive.ToString());
    }

    [Theory]
    [InlineData("error=yes")]
    [InlineData("Error=true")]
    [InlineData("error=TrUe")]
    [InlineData("future=enabled")]
    [InlineData("skip=all;")]
    [InlineData("error=true;error=true;error=true;error=true")]
    public void InvalidCheckValuesRemainTypedButProjectionFails(string value)
    {
        string text = "#check=" + value + "\nFROM scratch\n";
        DockerfileParseResult parsed = Dockerfile.TryParse(text);
        Assert.True(parsed.Success);
        CheckDirective directive = Assert.IsType<CheckDirective>(parsed.Dockerfile!.Items[0]);
        Assert.False(directive.TryGetOptions(out CheckDirectiveOptions? options, out string? error));
        Assert.Null(options);
        Assert.NotEmpty(error!);
        Assert.Equal(text, parsed.Dockerfile.ToString());
    }

    [Fact]
    public void CheckExtractionKeepsSpaceSuffixButInterpretsOnlyEffectiveValue()
    {
        CheckDirective directive = new("skip=Foo, Bar;error=true");
        Assert.True(directive.TryGetOptions(out CheckDirectiveOptions? options, out string? error), error);
        Assert.Equal(new[] { "Foo", "" }, options!.SkippedChecks);
        Assert.False(options.WarningsAsErrors);
        Assert.Equal("#check=skip=Foo, Bar;error=true", directive.ToString());
    }

    [Fact]
    public void StructuredCheckConstructionCopiesCollections()
    {
        List<string> names = new() { "FutureCheck" };
        CheckDirectiveOptions options = new(names, warningsAsErrors: true, experimentalAll: true);
        names.Clear();
        CheckDirective directive = new(options);
        Assert.Equal("#check=skip=FutureCheck;experimental=all;error=true", directive.ToString());
        Assert.True(directive.TryGetOptions(out CheckDirectiveOptions? parsed, out string? error), error);
        Assert.Equal(options.SkippedChecks, parsed!.SkippedChecks);
        Assert.True(parsed.ExperimentalAll);
    }

    [Theory]
    [InlineData("")]
    [InlineData("all")]
    [InlineData("Foo,Bar")]
    [InlineData("Foo;error=true")]
    [InlineData("Foo Bar")]
    public void StructuredCheckNamesCannotInjectOptions(string name)
    {
        Assert.Throws<ArgumentException>(() => new CheckDirectiveOptions(new[] { name }));
        Assert.Throws<ArgumentException>(() => new CheckDirectiveOptions(experimentalChecks: new[] { name }));
    }

    [Fact]
    public void TypedBuilderPreservesSeparatorsAndEffectiveHeader()
    {
        DockerfileBuilder builder = new() { EscapeChar = '`', CommentSeparator = "\t", DefaultNewLine = "\r\n" };
        builder.SyntaxDirective("docker/dockerfile:99.123")
            .EscapeDirective('`')
            .CheckDirective(new CheckDirectiveOptions(skipAll: true))
            .FromInstruction("scratch");
        // Automatic insertion already supplied the escape, so the explicit duplicate is still detectable.
        Assert.Throws<ParseException>(() => Dockerfile.Parse(builder.ToString()));
        Assert.IsType<EscapeDirective>(builder.Dockerfile.Items[0]);
        Assert.IsType<SyntaxDirective>(builder.Dockerfile.Items[2]);
        Assert.Equal('`', builder.Dockerfile.EscapeChar);

        DockerfileBuilder valid = new() { EscapeChar = '`', CommentSeparator = "\t", DefaultNewLine = "\r\n" };
        valid.EscapeDirective('`').SyntaxDirective("docker/dockerfile:99.123")
            .CheckDirective("skip=all").FromInstruction("scratch");
        Dockerfile reparsed = Dockerfile.Parse(valid.ToString());
        Assert.Equal(valid.ToString(), reparsed.ToString());
        Assert.Equal(valid.Dockerfile.Frontend.Reference, reparsed.Frontend.Reference);
        Assert.Equal('`', valid.Dockerfile.EscapeChar);
        valid.Dockerfile.Items.Insert(0, new Comment("header"));
        valid.Dockerfile.Items.Insert(1, new Whitespace("\n"));
        Assert.Equal('\\', valid.Dockerfile.EscapeChar);
        Assert.Equal(DockerfileFrontendKind.Bundled, valid.Dockerfile.Frontend.Kind);
    }

    [Fact]
    public void GenericBuilderUsesTypedFactoriesAndCaseInsensitiveEscapeChecks()
    {
        DockerfileBuilder builder = new() { EscapeChar = '`' };
        builder.ParserDirective("ESCAPE", "`");
        Assert.Single(builder.Dockerfile.Items.OfType<ParserDirective>());
        Assert.IsType<EscapeDirective>(builder.Dockerfile.Items[0]);
        Assert.Throws<InvalidOperationException>(() => builder.ParserDirective("EsCaPe", "\\"));
    }

    [Fact]
    public void TypedTokenCallbacksPreserveCustomFormatting()
    {
        DockerfileBuilder builder = new() { EscapeChar = '`', DefaultNewLine = "\n" };
        builder.EscapeDirective(tokens => tokens.String("# ESCAPE = `"))
            .SyntaxDirective(tokens => tokens.String(" # syntax = docker/dockerfile:1.20 "))
            .CheckDirective(tokens => tokens.String("#check=error=true"))
            .FromInstruction("scratch");
        Assert.Equal("# ESCAPE = `\n # syntax = docker/dockerfile:1.20 \n#check=error=true\nFROM scratch\n",
            builder.ToString());
        Assert.Single(builder.Dockerfile.Items.OfType<EscapeDirective>());
        Assert.Single(builder.Dockerfile.Items.OfType<SyntaxDirective>());
        Assert.Single(builder.Dockerfile.Items.OfType<CheckDirective>());
        Assert.Equal(builder.ToString(), Dockerfile.Parse(builder.ToString()).ToString());
    }

    [Fact]
    public void DirectiveDocumentationExamplesExposeOnlyDeclaredMetadata()
    {
        Dockerfile file = Dockerfile.Parse("#syntax=docker/dockerfile:1.20.0-labs\nFROM scratch\n");
        DockerfileFrontendMetadata frontend = file.Frontend;
        Assert.Equal("docker/dockerfile", frontend.Image);
        Assert.Equal("1.20.0-labs", frontend.Tag);
        Assert.Equal("1.20.0", frontend.Version);
        Assert.Equal(DockerfileFrontendChannel.Labs, frontend.Channel);

        CheckDirective check = CheckDirective.Parse(
            "#check=skip=JSONArgsRecommended,FutureCheck;experimental=all;error=true");
        Assert.True(check.TryGetOptions(out CheckDirectiveOptions? options, out string? error), error);
        Assert.True(options!.WarningsAsErrors);
        Assert.True(options.ExperimentalAll);
    }
}
