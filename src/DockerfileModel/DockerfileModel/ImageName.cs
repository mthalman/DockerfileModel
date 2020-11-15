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
        private RegistryToken? registryToken;
        private RepositoryToken repositoryToken;
        private TagToken? tagToken;
        private DigestToken? digestToken;

        internal ImageName(IEnumerable<Token> tokens) : base(tokens)
        {
            registryToken = Tokens.OfType<RegistryToken>().FirstOrDefault();
            repositoryToken = Tokens.OfType<RepositoryToken>().First();
            tagToken = Tokens.OfType<TagToken>().FirstOrDefault();
            digestToken = Tokens.OfType<DigestToken>().FirstOrDefault();
        }

        public static ImageName Create(string repository, string? registry = null, string? tag = null, string? digest = null)
        {
            Requires.NotNullOrWhiteSpace(repository, nameof(repository));
            Requires.ValidState(
                (tag is null && digest is null) || String.IsNullOrEmpty(tag) ^ String.IsNullOrEmpty(digest),
                $"Either {nameof(tag)} may be set or {nameof(digest)} may be set but not both.");

            StringBuilder builder = new StringBuilder();
            if (registry != null)
            {
                builder.Append(registry);
                builder.Append('/');
            }

            builder.Append(repository);

            if (tag != null)
            {
                builder.Append(':');
                builder.Append(tag);
            }
            else if (digest != null)
            {
                builder.Append('@');
                builder.Append(digest);
            }

            return Parse(builder.ToString());
        }

        public static ImageName Parse(string imageName) =>
            new ImageName(GetTokens(imageName, ImageNameParser.GetParser()));

        public static Parser<IEnumerable<Token>> GetParser() =>
            ImageNameParser.GetParser();

        public string? Registry
        {
            get => this.registryToken?.Value;
            set
            {
                RegistryToken? registryToken = RegistryToken;
                if (registryToken != null && value is not null)
                {
                    registryToken.Value = value;
                }
                else
                {
                    RegistryToken = String.IsNullOrEmpty(value) ? null : new RegistryToken(value!);
                }
            }
        }

        public RegistryToken? RegistryToken
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

        public RepositoryToken RepositoryToken
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

                TagToken? tagToken = TagToken;
                if (tagToken != null && value is not null)
                {
                    tagToken.Value = value;
                }
                else
                {
                    TagToken = String.IsNullOrEmpty(value) ? null : new TagToken(value!);
                }
            }
        }

        public TagToken? TagToken
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

                DigestToken? digestToken = DigestToken;
                if (digestToken != null && value is not null)
                {
                    digestToken.Value = value;
                }
                else
                {
                    DigestToken = String.IsNullOrEmpty(value) ? null : new DigestToken(value!);
                }
            }
        }

        public DigestToken? DigestToken
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

        private static class ImageNameParser
        {
            public static Parser<IEnumerable<Token>> GetParser() =>
                from registryRepository in RegistryRepository()
                from tagDigest in TagDigest().Optional()
                select ConcatTokens(
                    registryRepository, tagDigest.IsDefined ? tagDigest.GetOrDefault() : Enumerable.Empty<Token?>());

            private static Parser<Token> Registry() =>
                from identifier in DelimitedIdentifier(
                    Sprache.Parse.LetterOrDigit,
                    NameComponentChar().Except(Sprache.Parse.Char('.')),
                    '.',
                    minimumDelimiters: 1)
                select new RegistryToken(identifier);

            private static Parser<Token> Repository() =>
                from identifier in DelimitedIdentifier(Sprache.Parse.LetterOrDigit, NameComponentChar(), '/')
                select new RepositoryToken(identifier);

            private static Parser<IEnumerable<Token>> RegistryRepository() =>
                (from registry in Registry()
                 from separator in Symbol('/')
                 from repository in Repository()
                 select ConcatTokens(
                     registry,
                     separator,
                     repository)).Or(
                from repository in Repository()
                select new Token[] { repository });

            private static Parser<IEnumerable<Token>> Tag() =>
                from separator in Symbol(':')
                from tag in Sprache.Parse.Identifier(Sprache.Parse.LetterOrDigit, NameComponentChar())
                select ConcatTokens(
                    separator,
                    new TagToken(tag));

            private static Parser<IEnumerable<Token>> Digest() =>
                from digestSeparator in Symbol('@')
                from prefix in Sprache.Parse.String("sha")
                from digits in Sprache.Parse.Digit.Many().Text()
                from shaSeparator in Sprache.Parse.Char(':')
                from digest in Sprache.Parse.Identifier(Sprache.Parse.LetterOrDigit, Sprache.Parse.LetterOrDigit)
                select ConcatTokens(
                    digestSeparator,
                    new DigestToken($"sha{digits}:{digest}"));

            private static Parser<IEnumerable<Token>> TagDigest() =>
                (from tag in Tag()
                 select tag).Or(
                    from digest in Digest()
                    select digest);

            private static Parser<char> NameComponentChar() =>
                Sprache.Parse.LetterOrDigit
                    .Or(Sprache.Parse.Char('.'))
                    .Or(Sprache.Parse.Char('_'))
                    .Or(Sprache.Parse.Char('-'));
        }
    }

    public class RegistryToken : IdentifierToken
    {
        public RegistryToken(string value) : base(value)
        {
        }

        internal RegistryToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }
    }

    public class RepositoryToken : IdentifierToken
    {
        public RepositoryToken(string value) : base(value)
        {
        }

        internal RepositoryToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }
    }

    public class TagToken : IdentifierToken
    {
        public TagToken(string value) : base(value)
        {
        }

        internal TagToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }
    }

    public class DigestToken : IdentifierToken
    {
        public DigestToken(string value) : base(value)
        {
        }

        internal DigestToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }
    }
}
