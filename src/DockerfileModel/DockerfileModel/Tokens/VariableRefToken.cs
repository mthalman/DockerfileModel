using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel.Tokens
{
    public class VariableRefToken : AggregateToken
    {
        private static readonly string[] ValidModifiers = new string[] { ":-", ":+", ":?", "-", "+", "?" };

        /// <summary>
        /// Parsers for all of the variable substitution modifiers.
        /// </summary>
        private static readonly Parser<string>[] variableSubstitutionModifiers =
            ValidModifiers
                .Select(modifier => Sprache.Parse.String(modifier).Text())
                .ToArray();

        private VariableRefToken(string text, char escapeChar, CreateTokenParserDelegate createModifierValueToken)
            : base(text, GetInnerParser(escapeChar, createModifierValueToken))
        {
        }

        private VariableRefToken(IEnumerable<Token> tokens) : base(tokens)
        {
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
            set
            {
                LiteralToken? modifierValueToken = ModifierValueToken;
                if (modifierValueToken is not null && value is not null)
                {
                    modifierValueToken.Value = value;
                }
                else
                {
                    ModifierValueToken = String.IsNullOrEmpty(value) ? null : new LiteralToken(value!);
                }
            }
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
                this.ReplaceWithToken(new StringToken(value));
            }

            return value;
        }

        public static VariableRefToken Create(string variableName, bool includeBraces = false)
        {
            StringBuilder builder = new StringBuilder("$");
            if (includeBraces)
            {
                builder.Append("{");
            }
            builder.Append(variableName);
            if (includeBraces)
            {
                builder.Append("}");
            }

            return Parse(builder.ToString(), Dockerfile.DefaultEscapeChar);
        }

        public static VariableRefToken Create(string variableName, string modifier, string modifierValue) =>
            Parse($"${{{variableName}{modifier}{modifierValue}}}", Dockerfile.DefaultEscapeChar);

        public static VariableRefToken Parse(string text, char escapeChar) =>
            new VariableRefToken(text, escapeChar, (char escapeChar, IEnumerable<char> excludedChars) =>
                LiteralString(escapeChar, excludedChars));

        /// <summary>
        /// Parses a variable reference.
        /// </summary>
        /// <typeparam name="TPrimitiveToken">Type of the token for the variable.</typeparam>
        /// <param name="escapeChar">Escape character.</param>
        /// <param name="createModifierValueTokenParser">Delegate to create tokens nested within a modifier value.</param>
        /// <returns>Parsed variable reference token.</returns>
        public static Parser<VariableRefToken> GetParser(
            char escapeChar, CreateTokenParserDelegate createModifierValueTokenParser) =>
            from tokens in GetInnerParser(escapeChar, createModifierValueTokenParser)
            select new VariableRefToken(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(
            char escapeChar,
            CreateTokenParserDelegate createModifierValueTokenParser) =>
            SimpleVariableReference()
                .Or(BracedVariableReference(escapeChar, createModifierValueTokenParser));

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
        /// </summary>
        /// <param name="escapeChar">Escape character.</param>
        /// <param name="createModifierValueToken">Delegate to create a non-quoted token.</param>
        /// <returns>Parsed variable reference token.</returns>
        private static Parser<IEnumerable<Token>> BracedVariableReference(
            char escapeChar, CreateTokenParserDelegate createModifierValueToken) =>
            from variableChar in Sprache.Parse.Char('$')
            from opening in Symbol('{').AsEnumerable()
            from varNameToken in
                from varName in VariableIdentifier()
                select new StringToken(varName)
            from modifierTokens in (
                from modifier in OrConcat(variableSubstitutionModifiers).Once()
                from modifierValueTokens in ValueOrVariableRef(escapeChar, createModifierValueToken, new char[] { '}' })
                    .AtLeastOnce()
                    .Flatten()
                    .Where(tokens => tokens.Any())
                select ConcatTokens(
                    String.Concat(modifier).Select(ch => new SymbolToken(ch)),
                    new Token[] { new LiteralToken(modifierValueTokens) })
                ).Optional()
            from closing in Symbol('}').AsEnumerable()
            select ConcatTokens(opening, new Token[] { varNameToken }, modifierTokens.GetOrDefault(), closing);
    }
}
