using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;
using Validation;

namespace DockerfileModel.Tokens
{
    public class VariableRefToken : AggregateToken
    {
        public static readonly string[] ValidModifiers = new string[] { ":-", ":+", ":?", "-", "+", "?" };

        public VariableRefToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public VariableRefToken(string text, Parser<IEnumerable<Token?>> parser) : base(text, parser)
        {
        }

        public VariableRefToken(string text, Parser<Token> parser) : base(text, parser)
        {
        }

        public bool WrappedInBraces { get; internal set; }
        public string VariableName
        {
            get => ((PrimitiveToken)this.Tokens.First()).Value;
            set => ((PrimitiveToken)this.Tokens.First()).Value = value;
        }

        public string? Modifier => this.Tokens.OfType<SymbolToken>().FirstOrDefault()?.Value;
        public string? ModifierValue => ModifierValueToken?.ToString(TokenStringOptions.CreateOptionsForValueString());

        private VariableModifierValue? ModifierValueToken => this.Tokens.OfType<VariableModifierValue>().FirstOrDefault();

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            if (WrappedInBraces)
            {
                return $"${{{base.GetUnderlyingValue(options)}}}";
            }

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

        public void ClearVariableModifier()
        {
            if (this.TokenList.Count > 1)
            {
                this.TokenList.RemoveRange(1, 2);
            }
        }

        public void SetVariableModifier(string modifier, string modifierValue)
        {
            Requires.NotNull(modifier, nameof(modifier));
            Requires.NotNull(modifierValue, nameof(modifierValue));
            Requires.That(ValidModifiers.Contains(modifier), nameof(modifier), $"'{modifier}' is not a valid modifier.");

            SymbolToken modifierToken = new SymbolToken(modifier);
            StringToken modifierValueToken = new StringToken(modifierValue);
            if (this.TokenList.Count > 1)
            {
                this.TokenList[1] = modifierToken;
                this.TokenList[2] = modifierValueToken;
            }
            else
            {
                this.TokenList.Add(modifierToken);
                this.TokenList.Add(modifierValueToken);
            }
        }
    }

    public class VariableModifierValue : AggregateToken
    {
        public VariableModifierValue(IEnumerable<Token> tokens) : base(tokens)
        {
        }
    }
}
