using System;
using System.Collections.Generic;
using System.Linq;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;
using Validation;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel
{
    public class SecretMount : Mount
    {
        private readonly char escapeChar;

        public SecretMount(string id, string? destinationPath = null, string? environmentVariable = null, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(id, destinationPath, environmentVariable, escapeChar), escapeChar)
        {
        }

        internal SecretMount(IEnumerable<Token> tokens, char escapeChar)
            : base(tokens)
        {
            this.escapeChar = escapeChar;
        }

        public string Id
        {
            get => IdToken.Value;
            set
            {
                Requires.NotNull(value, nameof(value));
                IdToken.ValueToken.Value = value;
            }
        }

        public KeyValueToken<KeywordToken, LiteralToken> IdToken
        {
            get => Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>().Skip(1).First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(IdToken, value);
            }
        }

        public string? DestinationPath
        {
            get => DestinationPathToken?.Value;
            set
            {
                ValidateDestination(value, nameof(DestinationPath), EnvironmentVariable, nameof(EnvironmentVariable));
                SetDestinationValue("dst", DestinationPathToken, value, token => DestinationPathToken = token);
            }
        }

        public KeyValueToken<KeywordToken, LiteralToken>? DestinationPathToken
        {
            get => GetDestinationToken("dst");
            set
            {
                ValidateDestinationToken(value, nameof(DestinationPath), EnvironmentVariableToken, nameof(EnvironmentVariableToken));
                SetDestinationToken(DestinationPathToken, value);
            }
        }

        public string? EnvironmentVariable
        {
            get => EnvironmentVariableToken?.Value;
            set
            {
                ValidateDestination(DestinationPath, nameof(DestinationPath), value, nameof(EnvironmentVariable));
                SetDestinationValue("env", EnvironmentVariableToken, value, token => EnvironmentVariableToken = token);
            }
        }

        public KeyValueToken<KeywordToken, LiteralToken>? EnvironmentVariableToken
        {
            get => GetDestinationToken("env");
            set
            {
                ValidateDestinationToken(DestinationPathToken, nameof(DestinationPathToken), value, nameof(EnvironmentVariableToken));
                SetDestinationToken(EnvironmentVariableToken, value);
            }
        }

        private void SetDestinationValue(string destinationKey, KeyValueToken<KeywordToken, LiteralToken>? token, string? newValue,
            Action<KeyValueToken<KeywordToken, LiteralToken>?> setToken)
        {
            if (token is not null && newValue is not null)
            {
                token.ValueToken.Value = newValue;
            }
            else
            {
                setToken(String.IsNullOrEmpty(newValue) ?
                    null :
                    new KeyValueToken<KeywordToken, LiteralToken>(
                        new KeywordToken(destinationKey, escapeChar),
                        new LiteralToken(newValue!, canContainVariables: true, escapeChar)));
            }
        }

        private void SetDestinationToken(KeyValueToken<KeywordToken, LiteralToken>? currentValue, KeyValueToken<KeywordToken, LiteralToken>? newValue) =>
            SetToken(currentValue, newValue,
                addToken: token =>
                {
                    TokenList.Add(new SymbolToken(','));
                    TokenList.Add(token);
                },
                removeToken: token =>
                {
                    TokenList.RemoveRange(
                        TokenList.FirstPreviousOfType<Token, SymbolToken>(token),
                        token);
                });

        private KeyValueToken<KeywordToken, LiteralToken>? GetDestinationToken(string keyName) =>
            Tokens
                .OfType<KeyValueToken<KeywordToken, LiteralToken>>()
                .Skip(2)
                .FirstOrDefault(token => token.Key.Equals(keyName, StringComparison.OrdinalIgnoreCase));

        private static void ValidateDestination(
            string? destinationPath,
            string destinationPathName,
            string? environmentVariable,
            string environmentVariableName) =>
            Requires.ValidState(
                (destinationPath is null && environmentVariable is null) ||
                String.IsNullOrEmpty(destinationPath) ^ String.IsNullOrEmpty(environmentVariable),
                $"Either {destinationPathName} may be set or {environmentVariableName} may be set but not both.");

        private static void ValidateDestinationToken(
            KeyValueToken<KeywordToken, LiteralToken>? destinationPath,
            string destinationPathName,
            KeyValueToken<KeywordToken, LiteralToken>? environmentVariable,
            string environmentVariableName) =>
            Requires.ValidState(
                (destinationPath is null && environmentVariable is null) ||
                destinationPath is null ^ environmentVariable is null,
                $"Either {destinationPathName} may be set or {environmentVariableName} may be set but not both.");

        private static IEnumerable<Token> GetTokens(string id, string? destinationPath, string? environmentVariable,
            char escapeChar)
        {
            Requires.NotNullOrEmpty(id, nameof(id));
            ValidateDestination(destinationPath, nameof(destinationPath), environmentVariable, nameof(environmentVariable));

            string? destinationSegment = null;
            if (!String.IsNullOrEmpty(destinationPath))
            {
                destinationSegment = $",dst={destinationPath}";
            }

            string? envSegment = null;
            if (!String.IsNullOrEmpty(environmentVariable))
            {
                envSegment = $",env={environmentVariable}";
            }

            return GetTokens($"type=secret,id={id}{destinationSegment}{envSegment}", GetInnerParser(escapeChar));
        }

        public static SecretMount Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new SecretMount(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

        public static Parser<SecretMount> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new SecretMount(tokens, escapeChar);

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar)
        {
            Parser<LiteralToken> valueParser = LiteralWithVariables(
                escapeChar, new char[] { ',' });

            return
                from type in ArgTokens(
                    KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                        KeywordToken.GetParser("type", escapeChar), valueParser, escapeChar: escapeChar).AsEnumerable(), escapeChar)
                from comma in ArgTokens(Symbol(',').AsEnumerable(), escapeChar)
                from idDest in IdAndDestinationParser(valueParser, escapeChar).Or(IdParser(valueParser, escapeChar))
                select ConcatTokens(type, comma, idDest);
        }

        private static Parser<IEnumerable<Token>> IdAndDestinationParser(Parser<LiteralToken> valueParser, char escapeChar) =>
            from id in ArgTokens(
                KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                    KeywordToken.GetParser("id", escapeChar), valueParser, escapeChar: escapeChar).AsEnumerable(), escapeChar)
            from dest in ArgTokens(DestinationParser(escapeChar), escapeChar, excludeTrailingWhitespace: true)
            select ConcatTokens(id, dest);

        private static Parser<IEnumerable<Token>> IdParser(Parser<LiteralToken> valueParser, char escapeChar) =>
            KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                KeywordToken.GetParser("id", escapeChar), valueParser, escapeChar: escapeChar).AsEnumerable();

        private static Parser<IEnumerable<Token>> DestinationParser(char escapeChar) =>
            from comma in Symbol(',')
            from dst in KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                KeywordToken.GetParser("dst", escapeChar).Or(KeywordToken.GetParser("env", escapeChar)),
                LiteralWithVariables(escapeChar),
                escapeChar: escapeChar)
            select ConcatTokens(comma, dst);
    }
}
