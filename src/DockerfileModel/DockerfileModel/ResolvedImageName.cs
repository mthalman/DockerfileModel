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
    public class ResolvedImageName : AggregateToken
    {
        private RegistryToken? registryToken;
        private readonly RepositoryToken repositoryToken;
        private TagToken? tagToken;
        private DigestToken? digestToken;

        private ResolvedImageName(string text)
            : base(text, ImageNameParser.GetParser())
        {
            registryToken = Tokens.OfType<RegistryToken>().FirstOrDefault();
            repositoryToken = Tokens.OfType<RepositoryToken>().First();
            tagToken = Tokens.OfType<TagToken>().FirstOrDefault();
            digestToken = Tokens.OfType<DigestToken>().FirstOrDefault();
        }

        public static ResolvedImageName Create(string repository, string? registry = null, string? tag = null, string? digest = null)
        {
            Requires.NotNullOrWhiteSpace(repository, nameof(repository));
            Requires.ValidState(
                (tag is null && digest is null) || String.IsNullOrEmpty(tag) ^ String.IsNullOrEmpty(digest),
                $"Either {nameof(tag)} may be set or {nameof(digest)} may be set but not both.");

            StringBuilder builder = new StringBuilder();
            if (registry != null)
            {
                builder.Append(registry);
                builder.Append("/");
            }

            builder.Append(repository);

            if (tag != null)
            {
                builder.Append(":");
                builder.Append(tag);
            }
            else if (digest != null)
            {
                builder.Append("@");
                builder.Append(digest);
            }

            return Parse(builder.ToString());
        }

        public static ResolvedImageName Parse(string imageName) =>
            new ResolvedImageName(imageName);

        public static Parser<IEnumerable<Token>> GetParser() =>
            ImageNameParser.GetParser();

        public string? Registry
        {
            get => this.registryToken?.Value;
            set
            {
                if (this.registryToken is null && value is null)
                {
                    return;
                }

                if (this.registryToken is null)
                {
                    this.registryToken = new RegistryToken(value!);
                    this.TokenList.InsertRange(0, new Token[]
                    {
                        this.registryToken!,
                        new SymbolToken("/")
                    });
                }
                else
                {
                    if (value is null)
                    {
                        this.registryToken = null;
                        // Remove the registry and registry separator tokens
                        this.TokenList.RemoveRange(0, 2);
                    }
                    else
                    {
                        this.registryToken.Value = value;
                        this.TokenList[0] = this.registryToken;
                    }
                }
            }
        }

        public string Repository
        {
            get => repositoryToken.Value;
            set => repositoryToken.Value = value;
        }

        public string? Tag
        {
            get => this.tagToken?.Value;
            set
            {
                Requires.ValidState(
                    value is null || Digest is null,
                    $"{nameof(Tag)} cannot be set when {nameof(Digest)} is already set.");

                if (this.tagToken is null && value is null)
                {
                    return;
                }

                if (this.tagToken is null)
                {
                    this.tagToken = new TagToken(value!);
                    this.TokenList.AddRange(new Token[]
                    {
                        new SymbolToken(":"),
                        this.tagToken!
                    });
                }
                else
                {
                    if (value is null)
                    {
                        this.tagToken = null;
                        // Remove the tag separator and tag tokens
                        this.TokenList.RemoveRange(this.TokenList.Count - 2, 2);
                    }
                    else
                    {
                        this.tagToken.Value = value;
                        this.TokenList[this.TokenList.Count - 1] = this.tagToken;
                    }
                }
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

                if (this.digestToken is null && value is null)
                {
                    return;
                }

                if (this.digestToken is null)
                {
                    this.digestToken = new DigestToken(value!);
                    this.TokenList.AddRange(new Token[]
                    {
                        new SymbolToken("@"),
                        this.digestToken!
                    });
                }
                else
                {
                    if (value is null)
                    {
                        this.digestToken = null;
                        // Remove the digest separator and digest tokens
                        this.TokenList.RemoveRange(this.TokenList.Count - 2, 2);
                    }
                    else
                    {
                        this.digestToken.Value = value;
                        this.TokenList[this.TokenList.Count - 1] = this.digestToken;
                    }
                }
            }
        }

        private static class ImageNameParser
        {
            private static Parser<Token> Registry() =>
                from identifier in DelimitedIdentifier('.', minimumDelimiters: 1)
                select new RegistryToken(identifier);

            private static Parser<Token> Repository() =>
                from identifier in DelimitedIdentifier('/')
                select new RepositoryToken(identifier);

            private static Parser<IEnumerable<Token>> RegistryRepository() =>
                (from registry in Registry()
                 from separator in Symbol("/")
                 from repository in Repository()
                 select ConcatTokens(
                     registry,
                     separator,
                     repository)).Or(
                from repository in Repository()
                select new Token[] { repository });

            private static Parser<IEnumerable<Token>> Tag() =>
                from separator in Symbol(":")
                from tag in Sprache.Parse.Identifier(Sprache.Parse.LetterOrDigit, NonWhitespace())
                select ConcatTokens(
                    separator,
                    new TagToken(tag));

            private static Parser<IEnumerable<Token>> Digest() =>
                from digestSeparator in Symbol("@")
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

            public static Parser<IEnumerable<Token>> GetParser() =>
                from registryRepository in RegistryRepository()
                from tagDigest in TagDigest().Optional()
                select ConcatTokens(
                    registryRepository, tagDigest.IsDefined ? tagDigest.GetOrDefault() : Enumerable.Empty<Token?>());
        }
    }

    public class RegistryToken : IdentifierToken
    {
        public RegistryToken(string value) : base(value)
        {
        }
    }

    public class RepositoryToken : IdentifierToken
    {
        public RepositoryToken(string value) : base(value)
        {
        }

        public override string Value
        {
            get => base.Value;
            set
            {
                Requires.NotNullOrWhiteSpace(value, nameof(value));
                base.Value = value;
            }
        }
    }

    public class TagToken : IdentifierToken
    {
        public TagToken(string value) : base(value)
        {
        }
    }

    public class DigestToken : IdentifierToken
    {
        public DigestToken(string value) : base(value)
        {
        }
    }
}
