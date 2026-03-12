using System.Text;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel.Tokens;

public class VariableRefToken : AggregateToken
{
    // Ordering matters: longer modifiers must come before shorter prefixes (e.g., "##" before "#",
    // "%%" before "%", "//" before "/") so that the greedy parser matches the longest modifier first.
    // The default-value modifiers (":-", ":+", ":?", "-", "+", "?") are fully supported in
    // ResolveVariables. The POSIX modifiers ("##", "#", "%%", "%", "//", "/") are accepted for
    // parsing but not resolved, since Dockerfile variable resolution is limited compared to full
    // bash — see ResolveVariables for details.
    private static readonly string[] ValidModifiers = new string[] { ":-", ":+", ":?", "-", "+", "?", "##", "#", "%%", "%", "//", "/" };

    /// <summary>
    /// Parsers for all of the variable substitution modifiers.
    /// </summary>
    private static readonly Parser<string>[] variableSubstitutionModifiers =
        ValidModifiers
            .Select(modifier => Sprache.Parse.String(modifier).Text())
            .ToArray();
    private readonly char escapeChar;

    public VariableRefToken(string variableName, bool includeBraces = false, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(variableName, includeBraces, escapeChar), escapeChar)
    {
    }

    public VariableRefToken(string variableName, string modifier, string modifierValue,
        char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(variableName, modifier, modifierValue, escapeChar), escapeChar)
    {
    }

    internal VariableRefToken(IEnumerable<Token> tokens, char escapeChar) : base(tokens)
    {
        this.escapeChar = escapeChar;
    }

    public string VariableName
    {
        get => VariableNameToken.Value;
        set
        {
            Requires.NotNullOrEmpty(value, nameof(value));
            VariableNameToken.Value = value;
        }
    }

    public StringToken VariableNameToken
    {
        get => Tokens.OfType<StringToken>().First();
        set
        {
            Requires.NotNull(value, nameof(value));
            SetToken(VariableNameToken, value);
        }
    }

    public string? Modifier
    {
        get
        {
            string modifier = String.Concat(ModifierTokens.Select(token => token.Value));
            return modifier.Length > 0 ? modifier : null;
        }
        set
        {
            ValidateModifier(value);
            foreach (SymbolToken modifierToken in ModifierTokens.ToArray())
            {
                TokenList.Remove(modifierToken);
            }

            if (!String.IsNullOrEmpty(value))
            {
                TokenList.InsertRange(
                    TokenList.IndexOf(VariableNameToken) + 1,
                    value.Select(ch => new SymbolToken(ch)));
            }
            else
            {
                ModifierValueToken = null;
            }
        }
    }

    public IEnumerable<SymbolToken> ModifierTokens =>
        this.Tokens
            .OfType<SymbolToken>()
            .Where(token => token.Value != "{" && token.Value != "}");

    public string? ModifierValue
    {
        get => ModifierValueToken?.ToString(TokenStringOptions.CreateOptionsForValueString());
        set => SetOptionalLiteralTokenValue(ModifierValueToken, value, token => ModifierValueToken = token, canContainVariables: true, escapeChar);
    }

    public LiteralToken? ModifierValueToken
    {
        get => this.Tokens.OfType<LiteralToken>().FirstOrDefault();
        set
        {
            SetToken(ModifierValueToken, value,
                addToken: token =>
                {
                    TokenList.Insert(
                        ModifierTokens.Any() ?
                            TokenList.IndexOf(ModifierTokens.Last()) + 1 :
                            TokenList.IndexOf(VariableNameToken) + 1,
                        token);
                },
                removeToken: token =>
                {
                    TokenList.Remove(token);
                    Modifier = null;
                });
        }
    }

    protected override string GetUnderlyingValue(TokenStringOptions options)
    {
        return $"${base.GetUnderlyingValue(options)}";
    }

    public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
    {
        if (variables is null)
        {
            variables = new Dictionary<string, string?>();
        }

        if (options is null)
        {
            options = new ResolutionOptions();
        }

        string variableName = VariableName;
        string? modifier = Modifier;

        bool varExists = variables.TryGetValue(variableName, out string? value);

        if (modifier is not null)
        {
            // POSIX pattern modifiers (##, #, %%, %, //, /) require shell-level pattern
            // matching (glob patterns, substring removal/replacement) that goes beyond
            // Dockerfile variable resolution capabilities. Since Docker/BuildKit does not
            // support these modifiers, we return the raw variable reference text unchanged
            // rather than silently losing the modifier and its pattern.
            if (modifier == "#" || modifier == "##" ||
                modifier == "%" || modifier == "%%" ||
                modifier == "/" || modifier == "//")
            {
                return ToString();
            }

            bool isVariableSet;
            if (modifier[0] == ':')
            {
                isVariableSet = varExists && !String.IsNullOrEmpty(value);
            }
            else
            {
                isVariableSet = varExists;
            }

            switch (modifier.Last())
            {
                case '-':
                    if (!isVariableSet)
                    {
                        value = ModifierValueToken!.ResolveVariables(escapeChar, variables, options);
                    }
                    break;
                case '+':
                    if (!isVariableSet)
                    {
                        value = null;
                    }
                    else
                    {
                        value = ModifierValueToken!.ResolveVariables(escapeChar, variables, options);
                    }
                    break;
                case '?':
                    if (!isVariableSet)
                    {
                        string? errorDetail = ModifierValueToken!.ResolveVariables(escapeChar, variables, options);
                        throw new VariableSubstitutionException(
                            $"Variable '{variableName}' is not set. Error detail: '{errorDetail ?? "<empty>"}'.");
                    }
                    break;
                default:
                    break;
            }
        }

        value = options.FormatValue(escapeChar, value ?? String.Empty);
            
        if (options.UpdateInline)
        {
            if (String.IsNullOrEmpty(value))
            {
                this.TokenList.Clear();
            }
            else
            {
                this.ReplaceWithToken(new StringToken(value));
            }
        }

        return value;
    }

    public static VariableRefToken Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    /// <summary>
    /// Parses a variable reference.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <returns>Parsed variable reference token.</returns>
    public static Parser<VariableRefToken> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new VariableRefToken(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string variableName, string modifier, string modifierValue, char escapeChar)
    {
        Requires.NotNullOrEmpty(variableName, nameof(variableName));
        Requires.NotNullOrEmpty(modifier, nameof(modifier));
        Requires.NotNullOrEmpty(modifierValue, nameof(modifierValue));
        ValidateModifier(modifier);

        return GetTokens($"${{{variableName}{modifier}{modifierValue}}}", GetInnerParser(escapeChar));
    }

    private static IEnumerable<Token> GetTokens(string variableName, bool includeBraces, char escapeChar)
    {
        Requires.NotNullOrEmpty(variableName, nameof(variableName));

        StringBuilder builder = new("$");
        if (includeBraces)
        {
            builder.Append('{');
        }
        builder.Append(variableName);
        if (includeBraces)
        {
            builder.Append('}');
        }

        return GetTokens(builder.ToString(), GetInnerParser(escapeChar));
    }

    private static void ValidateModifier(string? modifier)
    {
        if (!String.IsNullOrEmpty(modifier))
        {
            Verify.Operation(ValidModifiers.Contains(modifier),
                $"'{modifier}' is not a valid modifier. Supported modifiers: {String.Join(", ", ValidModifiers)}");
        }
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        SimpleVariableReference()
            .Or(BracedVariableReference(escapeChar));


    /// <summary>
    /// Parses a variable reference using the simple variable syntax.
    /// </summary>
    /// <returns>Parsed variable reference token.</returns>
    private static Parser<IEnumerable<Token>> SimpleVariableReference() =>
        from variableChar in Sprache.Parse.Char('$')
        from variableIdentifier in VariableIdentifier()
        select new Token[] { new StringToken(variableIdentifier) };

    /// <summary>
    /// Parses a variable reference using the braced variable syntax.
    /// Modifier values (the portion after the modifier symbol, e.g., "must set" in
    /// "${VAR:?must set}") are always parsed with <see cref="ModifierValueParser"/>,
    /// which allows horizontal whitespace within the braces.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <returns>Parsed variable reference token.</returns>
    private static Parser<IEnumerable<Token>> BracedVariableReference(
        char escapeChar) =>
        from variableChar in Sprache.Parse.Char('$')
        from opening in Symbol('{').AsEnumerable()
        from varNameToken in
            from varName in VariableIdentifier()
            select new StringToken(varName)
        from modifierTokens in (
            from modifier in variableSubstitutionModifiers.Aggregate((current, next) => current.Or(next)).Once()
            from modifierValueTokens in ValueOrVariableRef(escapeChar, ModifierValueParser(), new char[] { '}' })
                .AtLeastOnce()
                .Flatten()
                .Where(tokens => tokens.Any())
            select ConcatTokens(
                String.Concat(modifier).Select(ch => new SymbolToken(ch)),
                new Token[] { new LiteralToken(modifierValueTokens, canContainVariables: true, escapeChar) })
            ).Optional()
        from closing in Symbol('}').AsEnumerable()
        select ConcatTokens(opening, new Token[] { varNameToken }, modifierTokens.GetOrDefault(), closing);

    /// <summary>
    /// Creates a parser delegate for modifier values inside braces. Modifier values may
    /// contain whitespace (e.g., "${VAR:?must set}" or "${IMAGE:?must set}"), so this
    /// parser allows spaces in the message portion and reads until the closing brace.
    /// </summary>
    private static CreateTokenParserDelegate ModifierValueParser() =>
        (char escapeChar, IEnumerable<char> excludedChars) => LiteralStringAllowingSpaces(escapeChar, excludedChars);
}
