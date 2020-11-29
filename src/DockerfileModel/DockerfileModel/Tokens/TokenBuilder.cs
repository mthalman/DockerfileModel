using System;
using System.Collections.Generic;
using System.Linq;

namespace DockerfileModel.Tokens
{
    public class TokenBuilder
    {
        public char EscapeChar { get; set; } = Dockerfile.DefaultEscapeChar;

        public string DefaultNewLine { get; set; } = Environment.NewLine;

        public IList<Token> Tokens { get; } = new List<Token>();

        public TokenBuilder ChangeOwner(string user, string? group = null) =>
            AddToken(new ChangeOwner(user, group, EscapeChar));

        public TokenBuilder Comment(string comment) =>
            AddToken(new CommentToken(comment));

        public TokenBuilder Comment(Action<TokenBuilder> configureBuilder) =>
            AddToken(new CommentToken(GetTokens(configureBuilder)));

        public TokenBuilder Digest(string digest) =>
            AddToken(new DigestToken(digest));

        public TokenBuilder Digest(Action<TokenBuilder> configureBuilder) =>
            AddToken(new DigestToken(GetTokens(configureBuilder)));

        public TokenBuilder ExecFormCommand(params string[] commands) =>
            AddToken(new ExecFormCommand(commands, EscapeChar));

        public TokenBuilder ExecFormCommand(Action<TokenBuilder> configureBuilder) =>
            AddToken(new ExecFormCommand(GetTokens(configureBuilder)));

        public TokenBuilder FromFlag(string fromStageName) =>
            AddToken(new FromFlag(fromStageName, EscapeChar));

        public TokenBuilder FromFlag(Action<TokenBuilder> configureBuilder) =>
            AddToken(new FromFlag(GetTokens(configureBuilder)));

        public TokenBuilder Identifier(string value) =>
            AddToken(new IdentifierToken(value));

        public TokenBuilder Identifier(Action<TokenBuilder> configureBuilder) =>
            AddToken(new IdentifierToken(GetTokens(configureBuilder)));

        public TokenBuilder ImageName(string repository, string? registry = null, string? tag = null, string? digest = null) =>
            AddToken(new ImageName(repository, registry, tag, digest));

        public TokenBuilder ImageName(Action<TokenBuilder> configureBuilder) =>
            AddToken(new ImageName(GetTokens(configureBuilder)));

        public TokenBuilder IntervalFlag(string interval) =>
            AddToken(new IntervalFlag(interval, EscapeChar));

        public TokenBuilder IntervalFlag(Action<TokenBuilder> configureBuilder) =>
            AddToken(new IntervalFlag(GetTokens(configureBuilder)));

        public TokenBuilder KeyValue<TKey, TValue>(TKey key, TValue value, bool isFlag = false,
            char separator = KeyValueToken<TKey, TValue>.DefaultSeparator)
            where TKey : Token, IValueToken
            where TValue : Token =>
            AddToken(new KeyValueToken<TKey, TValue>(key, value, isFlag, separator));

        public TokenBuilder KeyValue<TKey, TValue>(Action<TokenBuilder> configureBuilder)
            where TKey : Token, IValueToken
            where TValue : Token =>
            AddToken(new KeyValueToken<TKey, TValue>(GetTokens(configureBuilder)));

        public TokenBuilder Keyword(string value) =>
            AddToken(new KeywordToken(value));

        public TokenBuilder Keyword(Action<TokenBuilder> configureBuilder) =>
            AddToken(new KeywordToken(GetTokens(configureBuilder)));

        public TokenBuilder LineContinuation() =>
            AddToken(new LineContinuationToken(DefaultNewLine, EscapeChar));

        public TokenBuilder LineContinuation(Action<TokenBuilder> configureBuilder) =>
            AddToken(new LineContinuationToken(GetTokens(configureBuilder)));

        public TokenBuilder Literal(string value, bool canContainVariables = false) =>
            AddToken(new LiteralToken(value, canContainVariables, EscapeChar));

        public TokenBuilder Literal(Action<TokenBuilder> configureBuilder, bool canContainVariables = false) =>
            AddToken(new LiteralToken(GetTokens(configureBuilder), canContainVariables, EscapeChar));

        public TokenBuilder MountFlag(Mount mount) =>
            AddToken(new MountFlag(mount));

        public TokenBuilder MountFlag(Action<TokenBuilder> configureBuilder) =>
            AddToken(new MountFlag(GetTokens(configureBuilder)));

        public TokenBuilder PlatformFlag(string platform) =>
            AddToken(new PlatformFlag(platform, EscapeChar));

        public TokenBuilder PlatformFlag(Action<TokenBuilder> configureBuilder) =>
            AddToken(new PlatformFlag(GetTokens(configureBuilder)));

        public TokenBuilder Registry(string registry) =>
            AddToken(new RegistryToken(registry));

        public TokenBuilder Registry(Action<TokenBuilder> configureBuilder) =>
            AddToken(new RegistryToken(GetTokens(configureBuilder)));

        public TokenBuilder Repository(string repository) =>
            AddToken(new RepositoryToken(repository));

        public TokenBuilder Repository(Action<TokenBuilder> configureBuilder) =>
            AddToken(new RepositoryToken(GetTokens(configureBuilder)));

        public TokenBuilder RetriesFlag(string retries) =>
            AddToken(new RetriesFlag(retries, EscapeChar));

        public TokenBuilder RetriesFlag(Action<TokenBuilder> configureBuilder) =>
            AddToken(new RetriesFlag(GetTokens(configureBuilder)));

        public TokenBuilder NewLine() =>
            AddToken(new NewLineToken(DefaultNewLine));

        public TokenBuilder SecretMount(string id, string? destinationPath = null) =>
            AddToken(new SecretMount(id, destinationPath, EscapeChar));

        public TokenBuilder SecretMount(Action<TokenBuilder> configureBuilder) =>
            AddToken(new SecretMount(GetTokens(configureBuilder), EscapeChar));

        public TokenBuilder ShellFormCommand(string command) =>
            AddToken(new ShellFormCommand(command, EscapeChar));

        public TokenBuilder ShellFormCommand(Action<TokenBuilder> configureBuilder) =>
            AddToken(new ShellFormCommand(GetTokens(configureBuilder)));

        public TokenBuilder StartPeriodFlag(string startPeriod) =>
            AddToken(new StartPeriodFlag(startPeriod, EscapeChar));

        public TokenBuilder StartPeriodFlag(Action<TokenBuilder> configureBuilder) =>
            AddToken(new StartPeriodFlag(GetTokens(configureBuilder)));

        public TokenBuilder String(string value) =>
            AddToken(new StringToken(value));

        public TokenBuilder Symbol(char EscapeChar) =>
            AddToken(new SymbolToken(EscapeChar));

        public TokenBuilder Tag(string tag) =>
            AddToken(new TagToken(tag));

        public TokenBuilder Tag(Action<TokenBuilder> configureBuilder) =>
            AddToken(new TagToken(GetTokens(configureBuilder)));

        public TokenBuilder TimeoutFlag(string timeout) =>
            AddToken(new TimeoutFlag(timeout, EscapeChar));

        public TokenBuilder TimeoutFlag(Action<TokenBuilder> configureBuilder) =>
            AddToken(new TimeoutFlag(GetTokens(configureBuilder)));

        public TokenBuilder VariableRef(string variableName, bool includeBraces = false) =>
            AddToken(new VariableRefToken(variableName, includeBraces, EscapeChar));

        public TokenBuilder VariableRef(string variableName, string modifier, string modifierValue) =>
            AddToken(new VariableRefToken(variableName, modifier, modifierValue, EscapeChar));

        public TokenBuilder VariableRef(Action<TokenBuilder> configureBuilder) =>
            AddToken(new VariableRefToken(GetTokens(configureBuilder), EscapeChar));

        public TokenBuilder Whitespace(string value) =>
            AddToken(new WhitespaceToken(value));

        public override string ToString() =>
            string.Concat(Tokens.Select(token => token.ToString()));

        private TokenBuilder AddToken(Token token)
        {
            Tokens.Add(token);
            return this;
        }

        private IEnumerable<Token> GetTokens(Action<TokenBuilder> configureBuilder)
        {
            TokenBuilder builder = new TokenBuilder
            {
                DefaultNewLine = DefaultNewLine,
                EscapeChar = EscapeChar
            };
            configureBuilder(builder);
            return builder.Tokens;
        }
    }
}
