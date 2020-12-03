using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DockerfileModel.Tokens;
using Sprache;
using Validation;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ImageName : AggregateToken
    {
        private InnerTokens.Registry? registryToken;
        private InnerTokens.Repository repositoryToken;
        private InnerTokens.Tag? tagToken;
        private InnerTokens.Digest? digestToken;

        public ImageName(string repository, string? registry = null, string? tag = null, string? digest = null,
            char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(repository, registry, tag, digest, escapeChar))
        {
        }

        internal ImageName(IEnumerable<Token> tokens) : base(tokens)
        {
            registryToken = Tokens.OfType<InnerTokens.Registry>().FirstOrDefault();
            repositoryToken = Tokens.OfType<InnerTokens.Repository>().First();
            tagToken = Tokens.OfType<InnerTokens.Tag>().FirstOrDefault();
            digestToken = Tokens.OfType<InnerTokens.Digest>().FirstOrDefault();
        }

        public string? Registry
        {
            get => this.registryToken?.Value;
            set
            {
                InnerTokens.Registry? registryToken = RegistryToken;
                if (registryToken is not null && value is not null)
                {
                    registryToken.Value = value;
                }
                else
                {
                    RegistryToken = String.IsNullOrEmpty(value) ? null : new InnerTokens.Registry(value!);
                }
            }
        }

        private InnerTokens.Registry? RegistryToken
        {
            get => this.registryToken;
            set
            {
                SetToken(RegistryToken, value,
                    addToken: token =>
                    {
                        this.registryToken = token;
                        this.TokenList.InsertRange(0, new Token[]
                        {
                            token,
                            new SymbolToken('/')
                        });
                    },
                    removeToken: _ =>
                    {
                        this.registryToken = null;
                        // Remove the registry and registry separator tokens
                        this.TokenList.RemoveRange(0, 2);
                    });
            }
        }

        public string Repository
        {
            get => RepositoryToken.Value;
            set
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                RepositoryToken.Value = value;
            }
        }

        private InnerTokens.Repository RepositoryToken
        {
            get => repositoryToken;
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(RepositoryToken, value);
                repositoryToken = value;
            }
        }

        public string? Tag
        {
            get => this.tagToken?.Value;
            set
            {
                Requires.ValidState(
                    value is null || Digest is null,
                    $"{nameof(Tag)} cannot be set when {nameof(Digest)} is already set.");

                InnerTokens.Tag? tagToken = TagToken;
                if (tagToken is not null && value is not null)
                {
                    tagToken.Value = value;
                }
                else
                {
                    TagToken = String.IsNullOrEmpty(value) ? null : new InnerTokens.Tag(value!);
                }
            }
        }

        private InnerTokens.Tag? TagToken
        {
            get => tagToken;
            set
            {
                Requires.ValidState(
                    value is null || DigestToken is null,
                    $"{nameof(TagToken)} cannot be set when {nameof(DigestToken)} is already set.");

                SetToken(TagToken, value,
                    addToken: token =>
                    {
                        this.tagToken = token;
                        this.TokenList.AddRange(new Token[]
                        {
                            new SymbolToken(':'),
                            token
                        });
                    },
                    removeToken: _ =>
                    {
                        this.tagToken = null;
                        // Remove the tag separator and tag tokens
                        this.TokenList.RemoveRange(this.TokenList.Count - 2, 2);
                    });
            }
        }

        public string? Digest
        {
            get => this.digestToken?.Value;
            set
            {
                Requires.ValidState(
                    value is null || Tag is null,
                    $"{nameof(Digest)} cannot be set when {nameof(Tag)} is already set.");

                InnerTokens.Digest? digestToken = DigestToken;
                if (digestToken is not null && value is not null)
                {
                    digestToken.Value = value;
                }
                else
                {
                    DigestToken = String.IsNullOrEmpty(value) ? null : new InnerTokens.Digest(value!);
                }
            }
        }

        private InnerTokens.Digest? DigestToken
        {
            get => digestToken;
            set
            {
                Requires.ValidState(
                    value is null || Tag is null,
                    $"{nameof(DigestToken)} cannot be set when {nameof(TagToken)} is already set.");

                SetToken(DigestToken, value,
                    addToken: token =>
                    {
                        this.digestToken = token;
                        this.TokenList.AddRange(new Token[]
                        {
                            new SymbolToken('@'),
                            token
                        });
                    },
                    removeToken: _ =>
                    {
                        this.digestToken = null;
                        // Remove the digest separator and digest tokens
                        this.TokenList.RemoveRange(this.TokenList.Count - 2, 2);
                    });
            }
        }

        public static string FormatImageName(string repository, string? registry, string? tag, string? digest)
        {
            Requires.NotNullOrWhiteSpace(repository, nameof(repository));
            Requires.ValidState(
                (tag is null && digest is null) || String.IsNullOrEmpty(tag) ^ String.IsNullOrEmpty(digest),
                $"Either {nameof(tag)} may be set or {nameof(digest)} may be set but not both.");

            StringBuilder builder = new StringBuilder();
            if (registry is not null)
            {
                builder.Append(registry);
                builder.Append('/');
            }

            builder.Append(repository);

            if (tag is not null)
            {
                builder.Append(':');
                builder.Append(tag);
            }
            else if (digest is not null)
            {
                builder.Append('@');
                builder.Append(digest);
            }

            return builder.ToString();
        }

        public static ImageName Parse(string imageName, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new ImageName(GetTokens(imageName, GetParser(escapeChar)));

        public static Parser<IEnumerable<Token>> GetParser(char escapeChar) =>
                from registryRepository in ParseRegistryRepository(escapeChar)
                from tagDigest in ParseTagDigest(escapeChar).Optional()
                select ConcatTokens(
                    registryRepository, tagDigest.IsDefined ? tagDigest.GetOrDefault() : Enumerable.Empty<Token?>());

        private static IEnumerable<Token> GetTokens(string repository, string? registry, string? tag, string? digest, char escapeChar) =>
            GetTokens(FormatImageName(repository, registry, tag, digest), GetParser(escapeChar));

        private static Parser<IEnumerable<Token>> ParseRegistryRepository(char escapeChar) =>
            (from registry in InnerTokens.Registry.GetParser(escapeChar)
             from separator in Symbol('/')
             from repository in InnerTokens.Repository.GetParser(escapeChar)
             select ConcatTokens(
                 registry,
                 separator,
                 repository)).Or<IEnumerable<Token>>(
            from repository in InnerTokens.Repository.GetParser(escapeChar)
            select new Token[] { repository });

        private static Parser<IEnumerable<Token>> ParseTag(char escapeChar) =>
            from separator in Symbol(':')
            from tag in InnerTokens.Tag.GetParser(escapeChar)
            select ConcatTokens(separator, tag);

        private static Parser<IEnumerable<Token>> ParseDigest(char escapeChar) =>
            from digestSeparator in Symbol('@')
            from digest in InnerTokens.Digest.GetParser(escapeChar)
            select ConcatTokens(digestSeparator, digest);

        private static Parser<IEnumerable<Token>> ParseTagDigest(char escapeChar) =>
            (from tag in ParseTag(escapeChar)
             select tag).Or(
                from digest in ParseDigest(escapeChar)
                select digest);

        private static class InnerTokens
        {
            public class Digest : LiteralToken
            {
                private readonly char escapeChar;

                public Digest(string value, char escapeChar = Dockerfile.DefaultEscapeChar)
                    : this(GetTokens(value, GetInnerParser(escapeChar)), escapeChar)
                {
                }

                internal Digest(IEnumerable<Token> tokens, char escapeChar)
                    : base(tokens, canContainVariables: true, escapeChar)
                {
                    this.escapeChar = escapeChar;
                }

                public static Digest Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
                    new Digest(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

                public static Parser<Digest> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
                    from tokens in GetInnerParser(escapeChar)
                    select new Digest(tokens, escapeChar);

                protected override IEnumerable<Token> GetInnerTokens(string value) =>
                    GetTokens(value, GetInnerParser(escapeChar));

                private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
                    from prefix in ArgTokens(
                        StringToken("sha", escapeChar), escapeChar)
                    from digits in ArgTokens(
                        StringTokenCharWithOptionalLineContinuation(escapeChar, Sprache.Parse.Digit), escapeChar).Many()
                    from shaSeparator in ArgTokens(
                        StringTokenCharWithOptionalLineContinuation(escapeChar, Sprache.Parse.Char(':')), escapeChar)
                    from digest in ArgTokens(
                        IdentifierString(escapeChar, Sprache.Parse.LetterOrDigit, Sprache.Parse.LetterOrDigit), escapeChar, excludeTrailingWhitespace: true)
                    select TokenHelper.CollapseStringTokens(ConcatTokens(
                        prefix,
                        TokenHelper.CollapseStringTokens(digits.Flatten()),
                        shaSeparator,
                        digest));
            }

            public class Tag : LiteralToken
            {
                private readonly char escapeChar;

                public Tag(string value, char escapeChar = Dockerfile.DefaultEscapeChar)
                    : this(GetTokens(value, GetInnerParser(escapeChar)), escapeChar)
                {
                }

                internal Tag(IEnumerable<Token> tokens, char escapeChar)
                    : base(tokens, canContainVariables: true, escapeChar)
                {
                    this.escapeChar = escapeChar;
                }

                public static Tag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
                    new Tag(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

                public static Parser<Tag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
                    from tokens in GetInnerParser(escapeChar)
                    select new Tag(tokens, escapeChar);

                protected override IEnumerable<Token> GetInnerTokens(string value) =>
                    GetTokens(value, GetInnerParser(escapeChar));

                private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
                    DelimitedIdentifier(escapeChar, FirstCharParser(), TailCharParser(), '/');

                private static Parser<char> FirstCharParser() => Sprache.Parse.LetterOrDigit;

                private static Parser<char> TailCharParser() =>
                    Sprache.Parse.LetterOrDigit
                        .Or(Sprache.Parse.Char('.'))
                        .Or(Sprache.Parse.Char('_'))
                        .Or(Sprache.Parse.Char('-'));
            }

            public class Repository : LiteralToken
            {
                private readonly char escapeChar;

                public Repository(string value, char escapeChar = Dockerfile.DefaultEscapeChar)
                    : this(GetTokens(value, GetInnerParser(escapeChar)), escapeChar)
                {
                }

                internal Repository(IEnumerable<Token> tokens, char escapeChar)
                    : base(tokens, canContainVariables: true, escapeChar)
                {
                    this.escapeChar = escapeChar;
                }

                public static Repository Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
                    new Repository(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

                public static Parser<Repository> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
                    from tokens in GetInnerParser(escapeChar)
                    select new Repository(tokens, escapeChar);

                protected override IEnumerable<Token> GetInnerTokens(string value) =>
                    GetTokens(value, GetInnerParser(escapeChar));

                private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
                    DelimitedIdentifier(escapeChar, FirstCharParser(), TailCharParser(), '/');

                private static Parser<char> FirstCharParser() => Sprache.Parse.LetterOrDigit;

                private static Parser<char> TailCharParser() =>
                    Sprache.Parse.LetterOrDigit
                        .Or(Sprache.Parse.Char('_'))
                        .Or(Sprache.Parse.Char('-'));
            }

            public class Registry : LiteralToken
            {
                private readonly char escapeChar;

                public Registry(string value, char escapeChar = Dockerfile.DefaultEscapeChar)
                    : this(GetTokens(value, GetInnerParser(escapeChar)), escapeChar)
                {
                }

                internal Registry(IEnumerable<Token> tokens, char escapeChar)
                    : base(tokens, canContainVariables: true, escapeChar)
                {
                    this.escapeChar = escapeChar;
                }

                public static Registry Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
                    new Registry(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

                public static Parser<Registry> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
                    from tokens in GetInnerParser(escapeChar)
                    select new Registry(tokens, escapeChar);

                protected override IEnumerable<Token> GetInnerTokens(string value) =>
                    GetTokens(value, GetInnerParser(escapeChar));

                private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
                    DelimitedIdentifier(
                        escapeChar,
                        FirstCharParser(),
                        TailCharParser(),
                        '.',
                        minimumDelimiters: 1);

                private static Parser<char> FirstCharParser() => Sprache.Parse.LetterOrDigit;

                private static Parser<char> TailCharParser() =>
                    Sprache.Parse.LetterOrDigit
                        .Or(Sprache.Parse.Char('_'))
                        .Or(Sprache.Parse.Char('-'));
            }
        }
    }
}
