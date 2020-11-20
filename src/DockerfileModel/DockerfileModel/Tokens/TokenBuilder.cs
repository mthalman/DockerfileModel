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
            AddToken(DockerfileModel.ChangeOwner.Create(user, group, EscapeChar));

        public TokenBuilder Comment(string comment) =>
            AddToken(CommentToken.Create(comment));

        public TokenBuilder Comment(Action<TokenBuilder> configureBuilder) =>
            AddToken(new CommentToken(GetTokens(configureBuilder)));

        public TokenBuilder Digest(string digest) =>
            AddToken(new DigestToken(digest));

        public TokenBuilder Digest(Action<TokenBuilder> configureBuilder) =>
            AddToken(new DigestToken(GetTokens(configureBuilder)));

        public TokenBuilder ExecFormCommand(params string[] commands) =>
            AddToken(DockerfileModel.ExecFormCommand.Create(commands, EscapeChar));

        public TokenBuilder ExecFormCommand(Action<TokenBuilder> configureBuilder) =>
            AddToken(new ExecFormCommand(GetTokens(configureBuilder)));

        public TokenBuilder Identifier(string value) =>
            AddToken(new IdentifierToken(value));

        public TokenBuilder Identifier(Action<TokenBuilder> configureBuilder) =>
            AddToken(new IdentifierToken(GetTokens(configureBuilder)));

        public TokenBuilder ImageName(string repository, string? registry = null, string? tag = null, string? digest = null) =>
            AddToken(DockerfileModel.ImageName.Create(repository, registry, tag, digest));

        public TokenBuilder ImageName(Action<TokenBuilder> configureBuilder) =>
            AddToken(new ImageName(GetTokens(configureBuilder)));

        public TokenBuilder KeyValue<TKey, TValue>(TKey key, TValue value, bool isFlag = false,
            char separator = KeyValueToken<TKey, TValue>.DefaultSeparator)
            where TKey : Token, IValueToken
            where TValue : Token =>
            AddToken(KeyValueToken<TKey, TValue>.Create(key, value, isFlag, separator));

        public TokenBuilder KeyValue<TKey, TValue>(Action<TokenBuilder> configureBuilder)
            where TKey : Token, IValueToken
            where TValue : Token =>
            AddToken(new KeyValueToken<TKey, TValue>(GetTokens(configureBuilder)));

        public TokenBuilder Keyword(string value) =>
            AddToken(new KeywordToken(value));

        public TokenBuilder Keyword(Action<TokenBuilder> configureBuilder) =>
            AddToken(new KeywordToken(GetTokens(configureBuilder)));

        public TokenBuilder LineContinuation() =>
            AddToken(LineContinuationToken.Create(DefaultNewLine, EscapeChar));

        public TokenBuilder LineContinuation(Action<TokenBuilder> configureBuilder) =>
            AddToken(new LineContinuationToken(GetTokens(configureBuilder)));

        public TokenBuilder Literal(string value) =>
            AddToken(new LiteralToken(value));

        public TokenBuilder Literal(Action<TokenBuilder> configureBuilder) =>
            AddToken(new LiteralToken(GetTokens(configureBuilder)));

        public TokenBuilder MountFlag(Mount mount) =>
            AddToken(DockerfileModel.MountFlag.Create(mount));

        public TokenBuilder MountFlag(Action<TokenBuilder> configureBuilder) =>
            AddToken(new MountFlag(GetTokens(configureBuilder)));

        public TokenBuilder PlatformFlag(string platform) =>
            AddToken(DockerfileModel.PlatformFlag.Create(platform));

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

        public TokenBuilder NewLine() =>
            AddToken(new NewLineToken(DefaultNewLine));

        public TokenBuilder SecretMount(string id, string? destinationPath = null) =>
            AddToken(DockerfileModel.SecretMount.Create(id, destinationPath, EscapeChar));

        public TokenBuilder SecretMount(Action<TokenBuilder> configureBuilder) =>
            AddToken(new SecretMount(GetTokens(configureBuilder)));

        public TokenBuilder ShellFormCommand(string command) =>
            AddToken(DockerfileModel.ShellFormCommand.Create(command, EscapeChar));

        public TokenBuilder ShellFormCommand(Action<TokenBuilder> configureBuilder) =>
            AddToken(new ShellFormCommand(GetTokens(configureBuilder)));

        public TokenBuilder String(string value) =>
            AddToken(new StringToken(value));

        public TokenBuilder Symbol(char EscapeChar) =>
            AddToken(new SymbolToken(EscapeChar));

        public TokenBuilder Tag(string tag) =>
            AddToken(new TagToken(tag));

        public TokenBuilder Tag(Action<TokenBuilder> configureBuilder) =>
            AddToken(new TagToken(GetTokens(configureBuilder)));

        public TokenBuilder VariableRef(string variableName, bool includeBraces = false) =>
            AddToken(VariableRefToken.Create(variableName, includeBraces, EscapeChar));

        public TokenBuilder VariableRef(string variableName, string modifier, string modifierValue) =>
            AddToken(VariableRefToken.Create(variableName, modifier, modifierValue, EscapeChar));

        public TokenBuilder VariableRef(Action<TokenBuilder> configureBuilder) =>
            AddToken(new VariableRefToken(GetTokens(configureBuilder)));

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
