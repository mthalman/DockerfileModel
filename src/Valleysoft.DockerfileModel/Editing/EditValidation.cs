using System.Collections.Concurrent;
using System.Reflection;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>Supplies shared policy, live-tree ownership, and serialized-grammar checks for structural edits.</summary>
internal static class EditValidation
{
    private static readonly MethodInfo underlyingValueSlot = typeof(Token).GetMethod(
        "GetUnderlyingValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo documentStringSlot = typeof(Dockerfile).GetMethod(
        nameof(ToString), Type.EmptyTypes)!.GetBaseDefinition();
    private static readonly ConcurrentDictionary<Type, bool> supportedImplementations = new();
    private static readonly ConcurrentDictionary<Type, bool> supportedDocuments = new();

    /// <summary>Rejects unsupported serialization and existing cycles before an edit reads the live document text.</summary>
    /// <param name="document">The owner to inspect without invoking its virtual serializer.</param>
    /// <remarks>
    /// Legacy construction can contain unsupported tokens or cycles. Acyclic sharing is left for
    /// staged ownership validation so removing a shared occurrence can repair the document.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The document contains a cycle or unsupported serialization behavior.</exception>
    internal static void ValidateSupportedDocument(Dockerfile document)
    {
        if (!supportedDocuments.GetOrAdd(document.GetType(), type =>
            EffectiveImplementation(type, documentStringSlot)?.DeclaringType == typeof(Dockerfile)))
        {
            throw new InvalidOperationException("Structural editing requires inherited built-in document serialization.");
        }
        HashSet<Token> active = new(ReferenceComparer<Token>.Instance);
        HashSet<Token> completed = new(ReferenceComparer<Token>.Instance);
        Stack<(Token Token, bool Exiting)> pending = new();
        foreach (Token root in document.RawItems)
        {
            pending.Push((root, false));
        }
        while (pending.Count > 0)
        {
            (Token token, bool exiting) = pending.Pop();
            if (exiting)
            {
                active.Remove(token);
                completed.Add(token);
                continue;
            }
            if (completed.Contains(token))
            {
                continue;
            }
            if (!active.Add(token))
            {
                throw new InvalidOperationException("The document contains a token cycle.");
            }
            ValidateSupportedToken(token);
            pending.Push((token, true));
            if (token is AggregateToken aggregate)
            {
                foreach (Token child in aggregate.Tokens)
                {
                    pending.Push((child, false));
                }
            }
        }
    }

    /// <summary>Rejects effective token implementations that staged rendering and publication cannot safely reproduce.</summary>
    /// <param name="token">The token to inspect without invoking its serializer or quote accessors.</param>
    /// <remarks>
    /// Inheriting built-in behavior is supported, including through consumer subclasses. The
    /// effective Token serialization slot and both quote-interface accessors must remain built-in;
    /// unrelated hidden members do not change that dispatch. This check does not inspect descendants.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The token supplies unsupported serialization or quote behavior.</exception>
    internal static void ValidateSupportedToken(Token token)
    {
        if (!supportedImplementations.GetOrAdd(token.GetType(), HasSupportedImplementation))
        {
            throw new InvalidOperationException(
                "Structural editing requires inherited built-in token serialization and quote implementations.");
        }
    }

    /// <summary>Checks quote publication without calling consumer accessors, including setters that could mutate and then throw.</summary>
    /// <param name="token">The object whose quote is about to be staged or published.</param>
    /// <exception cref="InvalidOperationException">The object is not a supported token implementation.</exception>
    internal static void ValidateSupportedQuote(IQuotableToken token)
    {
        if (token is not Token value)
        {
            throw new InvalidOperationException("Structural quote changes require a supported token.");
        }
        ValidateSupportedToken(value);
    }

    private static bool HasSupportedImplementation(Type type)
    {
        if (!HasSupportedSerializer(type))
        {
            return false;
        }
        if (!typeof(IQuotableToken).IsAssignableFrom(type))
        {
            return true;
        }
        InterfaceMapping mapping = type.GetInterfaceMap(typeof(IQuotableToken));
        return mapping.TargetMethods.All(method =>
            method.DeclaringType == typeof(LiteralToken) || method.DeclaringType == typeof(IdentifierToken));
    }

    private static bool HasSupportedSerializer(Type type)
    {
        Type? declaringType = EffectiveImplementation(type, underlyingValueSlot)?.DeclaringType;
        return declaringType == typeof(AggregateToken) ||
            declaringType == typeof(VariableRefToken) ||
            declaringType == typeof(PrimitiveToken);
    }

    /// <summary>Finds the actual virtual-slot implementation rather than an unrelated member hiding the same name.</summary>
    /// <param name="type">The concrete consumer or built-in type.</param>
    /// <param name="slot">The base definition whose dispatch structural serialization uses.</param>
    /// <returns>The most-derived override of that slot, or null if none exists.</returns>
    private static MethodInfo? EffectiveImplementation(Type type, MethodInfo slot)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            MethodInfo? implementation = current.GetMethods(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(method => method.IsVirtual && method.GetBaseDefinition().Equals(slot));
            if (implementation is not null)
            {
                return implementation;
            }
        }
        return null;
    }

    /// <summary>Rejects undefined trivia policies before an edit is prepared.</summary>
    /// <param name="trivia">The caller-supplied policy.</param>
    public static void ValidateTrivia(TriviaDisposition trivia)
    {
        if (trivia is not TriviaDisposition.Preserve and not TriviaDisposition.Discard)
        {
            throw new ArgumentOutOfRangeException(nameof(trivia));
        }
    }

    /// <summary>Infers a fallback newline from the first line ending in text.</summary>
    /// <param name="text">The existing serialization used for inference.</param>
    /// <returns>CRLF when the first LF is preceded by CR; LF otherwise, including when no newline exists.</returns>
    /// <remarks>Boundary-sensitive edits should instead inspect the newline nearest their insertion seam.</remarks>
    public static string NewLine(string text)
    {
        int index = text.IndexOf('\n');
        return index > 0 && text[index - 1] == '\r' ? "\r\n" : "\n";
    }

    /// <summary>Rejects cycles and repeated token identities, and enforces supported implementations for editing.</summary>
    /// <param name="root">The live subtree whose ownership is being checked.</param>
    /// <param name="forEditing">Whether to enforce the structural implementation boundary; read-only traversal passes false.</param>
    /// <remarks>
    /// This check does not read staged child lists, validate escape context, or detect sharing
    /// with a separate tree. Adapters must check those additional constraints when adopting tokens.
    /// </remarks>
    public static void ValidateTree(Token root, bool forEditing = true)
    {
        HashSet<Token> seen = new(ReferenceComparer<Token>.Instance);
        Visit(root);

        void Visit(Token token)
        {
            if (forEditing)
            {
                ValidateSupportedToken(token);
            }
            if (!seen.Add(token))
            {
                throw new InvalidOperationException("The edited token tree contains a cycle or a shared token.");
            }
            if (token is AggregateToken aggregate)
            {
                foreach (Token child in aggregate.Tokens)
                {
                    Visit(child);
                }
            }
        }
    }

    /// <summary>Rejects incompatible escape contexts in a live subtree before adoption.</summary>
    /// <param name="token">The root of a previously validated acyclic subtree, included in the check.</param>
    /// <param name="escapeChar">The destination owner's effective escape character.</param>
    /// <remarks>
    /// Only context-bearing operands, commands, and instructions are checked; context-independent
    /// trivia and punctuation do not constrain adoption. Heredoc delimiters store raw text without
    /// document escape parsing, so their inherited metadata is also ignored. This check does not read staged children.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A context-bearing token uses a different escape character.</exception>
    internal static void ValidateEscapeContext(Token token, char escapeChar)
    {
        if (InstructionCollectionEditing.Descendants(token).OfType<AggregateToken>().Any(candidate =>
            candidate is not HeredocDelimiterToken &&
            candidate is IValueToken or IKeyValuePair or VariableRefToken or Mount or ImageName or Command or Instruction &&
            candidate.EditingEscapeChar != escapeChar))
        {
            throw new InvalidOperationException("A nested operand uses an incompatible escape character.");
        }
    }

    /// <summary>Parses one complete instruction using diagnostic framing and its effective escape context.</summary>
    /// <param name="text">The prospective instruction serialization.</param>
    /// <param name="escapeChar">The owner's effective escape character.</param>
    /// <returns>The parsed instruction for further role, count, and value comparisons.</returns>
    /// <remarks>
    /// Diagnostic framing detects heredoc markers throughout a continued header and rejects
    /// missing terminators. Parsing success alone does not prove an edit preserves operand
    /// meaning; adapters compare the returned roles and heredoc associations before committing.
    /// </remarks>
    public static Instruction ParseInstruction(string text, char escapeChar)
    {
        Instruction parsed;
        try
        {
            ConstructReader.Region region = ConstructReader.Read(text, 0, escapeChar);
            if (region.UnterminatedMarker.HasValue || region.End != text.Length)
            {
                throw new InvalidOperationException("The edit would not form one complete instruction.");
            }
            string name = Instruction.InstructionNameParser(escapeChar).Parse(text);
            parsed = Instruction.CreateDiagnosticInstruction(name, text, escapeChar,
                new InstructionParseContext(region, 0));
        }
        catch (ParseException exception)
        {
            throw new InvalidOperationException("The edit would produce an invalid instruction.", exception);
        }
        if (parsed.ToString() != text)
        {
            throw new InvalidOperationException("The edited instruction was not consumed completely.");
        }
        return parsed;
    }

    /// <summary>Verifies that prospective parsing preserves explicitly modeled heredoc pairs and their raw bodies.</summary>
    /// <param name="owner">The live owner whose staged marker and body tokens define the expected roles.</param>
    /// <param name="parsed">The diagnostic interpretation of the complete prospective instruction.</param>
    /// <param name="plan">The plan supplying prospective children and token serialization.</param>
    /// <remarks>
    /// Ordinary source or command text must not become an unmodeled heredoc, even when the
    /// resulting serialization happens to include a valid delimiter. This check does not replace
    /// the live owner or its pair wrappers with parsed objects.
    /// </remarks>
    public static void ValidateHeredocAssociations(AggregateToken owner, Instruction parsed, TokenEditPlan plan)
    {
        Token[] expected = StagedDescendants(owner).ToArray();
        Token[] actual = InstructionCollectionEditing.Descendants(parsed).ToArray();
        HeredocMarkerToken[] expectedMarkers = expected.OfType<HeredocMarkerToken>().ToArray();
        HeredocBodyToken[] expectedBodies = expected.OfType<HeredocBodyToken>().ToArray();
        HeredocMarkerToken[] actualMarkers = actual.OfType<HeredocMarkerToken>().ToArray();
        HeredocBodyToken[] actualBodies = actual.OfType<HeredocBodyToken>().ToArray();
        if (expectedMarkers.Length != expectedBodies.Length ||
            actualMarkers.Length != expectedMarkers.Length || actualBodies.Length != expectedBodies.Length)
        {
            throw new InvalidOperationException("The edit changes modeled heredoc roles or pair counts.");
        }
        for (int index = 0; index < expectedMarkers.Length; index++)
        {
            HeredocMarkerToken expectedMarker = expectedMarkers[index];
            HeredocMarkerToken actualMarker = actualMarkers[index];
            if (plan.Render(expectedMarker) != actualMarker.ToString() ||
                expectedMarker.DelimiterName != actualMarker.DelimiterName ||
                expectedMarker.Expand != actualMarker.Expand || expectedMarker.Chomp != actualMarker.Chomp ||
                plan.Render(expectedBodies[index]) != actualBodies[index].ToString())
            {
                throw new InvalidOperationException("The edit changes heredoc associations or delimiter semantics.");
            }
        }

        IEnumerable<Token> StagedDescendants(Token token)
        {
            yield return token;
            if (token is AggregateToken aggregate)
            {
                foreach (Token child in plan.Tokens(aggregate))
                {
                    foreach (Token descendant in StagedDescendants(child))
                    {
                        yield return descendant;
                    }
                }
            }
        }
    }
}
