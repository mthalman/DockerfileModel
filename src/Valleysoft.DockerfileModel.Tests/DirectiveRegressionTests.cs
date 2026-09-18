using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class DirectiveRegressionTests
{
    [Theory]
    [InlineData("#unknown=value\\\n")]
    [InlineData(" # unknown=value\\ \r\n")]
    [InlineData("# ordinary comment\\\n")]
    [InlineData("#escape=`\n#unknown=value`\n")]
    [InlineData("#syntax=value\n\n#syntax=misplaced\\\n")]
    public void CommentTransitionPreservesFollowingInstruction(string header)
    {
        string text = header + "FROM scratch\n";
        foreach (Dockerfile file in new[] { Dockerfile.Parse(text), Dockerfile.TryParse(text).Dockerfile! })
        {
            Assert.Equal(text, file.ToString());
            Assert.Single(file.Items.OfType<FromInstruction>());
            SourceSpanTests.AssertPartition(text, file);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WhitespaceEditsHaveTheSameMeaningAsReparsedDeclarations(int edit)
    {
        SyntaxDirective syntax = new("docker/dockerfile:1");
        CheckDirective check = new("skip=all");
        EscapeDirective escape = new('\\');
        SetValue(syntax, " \tdocker/dockerfile:1.20 \t", edit);
        SetValue(check, " \terror=true \t", edit);
        SetValue(escape, " \t` \t", edit);
        string syntaxText = syntax.ToString();
        string checkText = check.ToString();
        string escapeText = escape.ToString();

        Assert.Equal(SyntaxDirective.Parse(syntaxText).Frontend.Reference, syntax.Frontend.Reference);
        Assert.Equal("docker/dockerfile:1.20", syntax.Frontend.Reference);
        Assert.True(check.TryGetOptions(out CheckDirectiveOptions? options, out string? error), error);
        Assert.True(options!.WarningsAsErrors);
        Assert.Equal('`', escape.EscapeChar);
        Assert.Equal(syntaxText, syntax.ToString());
        Assert.Equal(checkText, check.ToString());
        Assert.Equal(escapeText, escape.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("docker/dockerfile:1 note\nFROM scratch")]
    public void MalformedEditedSyntaxDirectiveDoesNotYieldValidMetadata(string value)
    {
        SyntaxDirective syntax = new("docker/dockerfile:1");
        syntax.DirectiveValueToken.Value = value;
        Assert.Throws<InvalidOperationException>(() => syntax.Frontend);
    }

    [Theory]
    [InlineData("error=true;experimental=all;skip=Foo;Bar")]
    [InlineData("error=true;skip=all;experimental=Foo;Bar")]
    public void ParsedCheckNamesThatCannotBeReorderedAreRejectedDuringConstruction(string value)
    {
        CheckDirective original = new(value);
        Assert.True(original.TryGetOptions(out CheckDirectiveOptions? options, out string? error), error);
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new CheckDirective(options!));
        Assert.Equal("options", exception.ParamName);
        Assert.Equal("#check=" + value, original.ToString());
    }

    [Theory]
    [InlineData("skip=all")]
    [InlineData("skip=;error=true")]
    [InlineData("experimental=Foo;skip=FutureCheck;error=true")]
    public void RepresentableCheckSnapshotsCanBeConstructedAgain(string value)
    {
        CheckDirective original = new(value);
        Assert.True(original.TryGetOptions(out CheckDirectiveOptions? before, out string? error), error);
        CheckDirective copy = new(before!);
        Assert.True(copy.TryGetOptions(out CheckDirectiveOptions? after, out error), error);
        Assert.Equal(before!.SkippedChecks, after!.SkippedChecks);
        Assert.Equal(before.SkipAll, after.SkipAll);
        Assert.Equal(before.ExperimentalChecks, after.ExperimentalChecks);
        Assert.Equal(before.ExperimentalAll, after.ExperimentalAll);
        Assert.Equal(before.WarningsAsErrors, after.WarningsAsErrors);
    }

    private static void SetValue(ParserDirective directive, string value, int edit)
    {
        switch (edit)
        {
            case 0:
                directive.DirectiveValue = value;
                break;
            case 1:
                directive.DirectiveValueToken.Value = value;
                break;
            default:
                Assert.Single(directive.DirectiveValueToken.Tokens.OfType<StringToken>()).Value = value;
                break;
        }
    }
}
