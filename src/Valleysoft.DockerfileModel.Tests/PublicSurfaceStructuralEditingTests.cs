using System.Reflection;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Locks down the collection-only editing surface and the compatibility contracts retained alongside it.
/// </summary>
public class PublicSurfaceStructuralEditingTests
{
    /// <summary>
    /// Enumerates both semantic and token-backed properties whose declared type must expose structural editing.
    /// </summary>
    /// <returns>Rows containing an owner type, property name, and expected collection element type.</returns>
    public static IEnumerable<object[]> Collections()
    {
        yield return Row(typeof(Dockerfile), "Items", typeof(DockerfileConstruct));
        yield return Row(typeof(FileTransferInstruction), "Sources", typeof(string));
        yield return Row(typeof(FileTransferInstruction), "SourceTokens", typeof(LiteralToken));
        yield return Row(typeof(FileTransferInstruction), "Heredocs", typeof(Heredoc));
        foreach (Type owner in new[] { typeof(CopyInstruction), typeof(AddInstruction) })
        {
            yield return Row(owner, "Excludes", typeof(string));
            yield return Row(owner, "ExcludeFlagTokens", typeof(ExcludeFlag));
        }
        yield return Row(typeof(RunInstruction), "Mounts", typeof(Mount));
        yield return Row(typeof(RunInstruction), "Heredocs", typeof(Heredoc));
        yield return Row(typeof(EnvInstruction), "Variables", typeof(IKeyValuePair));
        yield return Row(typeof(EnvInstruction), "VariableTokens", typeof(KeyValueToken<Variable, LiteralToken>));
        yield return Row(typeof(LabelInstruction), "Labels", typeof(IKeyValuePair));
        yield return Row(typeof(LabelInstruction), "LabelTokens", typeof(KeyValueToken<LabelKeyToken, LiteralToken>));
        yield return Row(typeof(ArgInstruction), "Args", typeof(IKeyValuePair));
        yield return Row(typeof(ArgInstruction), "ArgTokens", typeof(ArgDeclaration));
        yield return Row(typeof(ExposeInstruction), "Ports", typeof(string));
        yield return Row(typeof(ExposeInstruction), "PortTokens", typeof(LiteralToken));
        yield return Row(typeof(ExecFormCommand), "Values", typeof(string));
        yield return Row(typeof(ExecFormCommand), "ValueTokens", typeof(LiteralToken));
        yield return Row(typeof(VolumeInstruction), "Paths", typeof(string));
        yield return Row(typeof(VolumeInstruction), "PathTokens", typeof(LiteralToken));
        yield return Row(typeof(GenericInstruction), "ArgLines", typeof(string));
        yield return Row(typeof(Instruction), "Comments", typeof(string));
        yield return Row(typeof(Instruction), "CommentTokens", typeof(CommentToken));
        yield return Row(typeof(Mount), "Entries", typeof(MountEntry));
    }

    /// <summary>
    /// Prevents any supported property from exposing only a legacy list interface or a narrower concrete subtype.
    /// </summary>
    /// <param name="owner">The type declaring or inheriting the collection property.</param>
    /// <param name="property">The public collection property name.</param>
    /// <param name="element">The required editable-list element type.</param>
    [Theory]
    [MemberData(nameof(Collections))]
    public void EveryCollectionExposesTheDedicatedContract(Type owner, string property, Type element)
    {
        PropertyInfo? member = owner.GetProperty(property);
        Assert.NotNull(member);
        Assert.Equal(typeof(EditableList<>).MakeGenericType(element), member.PropertyType);
    }

    /// <summary>
    /// Guards the collection-only scope against reintroducing trivia-policy scalar methods or stage renaming.
    /// </summary>
    [Fact]
    public void TriviaPolicyMethodsBelongOnlyToCollections()
    {
        IEnumerable<MethodInfo> methods = typeof(Dockerfile).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(TriviaDisposition)));
        Assert.NotEmpty(methods);
        Assert.All(methods, method => Assert.Equal(typeof(EditableList<>), method.DeclaringType));
        Assert.Null(typeof(Dockerfile).GetMethod("RenameStage"));
    }

    /// <summary>
    /// Keeps one public removal signature per operation with preservation as its optional policy.
    /// </summary>
    /// <param name="name">The public operation whose overload set is constrained.</param>
    /// <param name="parameterCount">The complete signature including the optional policy.</param>
    [Theory]
    [InlineData("Remove", 2)]
    [InlineData("RemoveAt", 2)]
    [InlineData("Clear", 1)]
    public void RemovalMethodsExposeOneDefaultedPolicySignature(string name, int parameterCount)
    {
        MethodInfo method = Assert.Single(typeof(EditableList<string>)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == name);
        ParameterInfo[] parameters = method.GetParameters();
        Assert.Equal(parameterCount, parameters.Length);
        ParameterInfo policy = parameters.Last();
        Assert.Equal(typeof(TriviaDisposition), policy.ParameterType);
        Assert.True(policy.IsOptional);
        Assert.Equal(TriviaDisposition.Preserve, policy.DefaultValue);
    }

    /// <summary>
    /// Prevents index-based insertion and atomic movement from growing redundant anchored variants.
    /// </summary>
    /// <param name="name">An intentionally excluded convenience method.</param>
    [Theory]
    [InlineData("InsertBefore")]
    [InlineData("InsertAfter")]
    [InlineData("MoveBefore")]
    [InlineData("MoveAfter")]
    public void CollectionsExposeOnlyIndexedInsertionAndMovement(string name)
    {
        Type collection = typeof(EditableList<string>);
        Assert.Null(collection.GetMethod(name));
        Assert.NotNull(collection.GetMethod("Insert", [typeof(int), typeof(string)]));
        Assert.NotNull(collection.GetMethod("Move", [typeof(int), typeof(int)]));
    }

    /// <summary>
    /// Preserves subclass compatibility by requiring the old protected constructor alongside its context-aware overload.
    /// </summary>
    /// <param name="owner">The extensible base type whose protected constructors are inspected.</param>
    [Theory]
    [InlineData(typeof(Instruction))]
    [InlineData(typeof(Command))]
    [InlineData(typeof(CommandInstruction))]
    [InlineData(typeof(GenericInstruction))]
    public void ContextAwareProtectedConstructorsRetainOldOverload(Type owner)
    {
        foreach (Type[] parameters in new[]
        {
            new[] { typeof(IEnumerable<Token>) },
            new[] { typeof(IEnumerable<Token>), typeof(char) }
        })
        {
            ConstructorInfo? constructor = owner.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance, null, parameters, null);
            Assert.NotNull(constructor);
            Assert.True(constructor.IsFamily);
        }
    }

    /// <summary>
    /// Preserves legacy interface contracts while exposing editable concrete collections and a protected comment helper.
    /// </summary>
    [Fact]
    public void ExistingCommentableInterfaceRemainsImplementable()
    {
        Assert.Equal(typeof(IList<string>), typeof(ICommentable).GetProperty("Comments")!.PropertyType);
        Assert.Equal(typeof(IEnumerable<CommentToken>), typeof(ICommentable).GetProperty("CommentTokens")!.PropertyType);
        Assert.True(typeof(IList<LiteralToken>).IsAssignableFrom(typeof(TokenList<LiteralToken>)));
        Assert.True(typeof(EditableList<LiteralToken>).IsAssignableFrom(typeof(TokenList<LiteralToken>)));
        MethodInfo? getComments = typeof(AggregateToken).GetMethod(
            "GetComments", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(getComments);
        Assert.True(getComments.IsFamily);
        Assert.Equal(typeof(EditableList<string>), getComments.ReturnType);
    }

    /// <summary>
    /// Requires the constructors and comment anchors needed to create and insert complete collection elements.
    /// </summary>
    [Fact]
    public void CollectionConstructionAndCommentAnchorsArePresent()
    {
        Assert.NotNull(typeof(Instruction).GetMethod("InsertCommentBefore", [typeof(Token), typeof(string)]));
        Assert.NotNull(typeof(Instruction).GetMethod("InsertCommentAfter", [typeof(Token), typeof(string)]));
        Assert.NotNull(typeof(MountEntry).GetConstructor([typeof(string), typeof(string), typeof(char)]));
        Assert.NotNull(typeof(Heredoc).GetConstructor(
            [typeof(string), typeof(string), typeof(HeredocQuoteKind), typeof(bool), typeof(char)]));
        Assert.Equal(typeof(string), typeof(Heredoc).GetProperty("RawContent")!.PropertyType);
    }

    /// <summary>
    /// Ensures convenience parsers and parser factories both retain the escape context needed for later adoption.
    /// </summary>
    [Fact]
    public void FlagParserFactoriesRetainTheirExplicitEscapeContext()
    {
        AggregateToken[] flags =
        [
            FromFlag.Parse("--from=builder", '`'),
            FromFlag.GetParser('`')(new Input("--from=builder")).Value,
            ChangeOwnerFlag.Parse("--chown=root", '`'),
            ChangeOwnerFlag.GetParser('`')(new Input("--chown=root")).Value
        ];
        PropertyInfo? context = typeof(AggregateToken).GetProperty(
            "EditingEscapeChar", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(context);
        Assert.All(flags, flag => Assert.Equal('`', context.GetValue(flag)));
    }

    private static object[] Row(Type owner, string property, Type element) => [owner, property, element];
}
