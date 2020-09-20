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
        private TagToken? tagToken;
        private DigestToken? digestToken;

        private ImageName(string text)
            : base(text, ImageNameParser.GetParser())
        {
            registryToken = Tokens.OfType<RegistryToken>().FirstOrDefault();
            Repository = Tokens.OfType<RepositoryToken>().First();
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

        public static ImageName Parse(string imageName) =>
            new ImageName(imageName);

        public static Parser<IEnumerable<Token>> GetParser() =>
            ImageNameParser.GetParser();

        public RegistryToken? Registry
        {
            get => this.registryToken;
            set
            {
                if (this.registryToken is null && value is null)
                {
                    return;
                }

                if (this.registryToken is null)
                {
                    this.registryToken = value;
                    this.TokenList.InsertRange(0, new Token[]
                    {
                        this.registryToken!,
                        new SymbolToken("/")
                    });
                }
                else
                {
                    this.registryToken = value;

                    if (this.registryToken is null)
                    {
                        // Remove the registry and registry separator tokens
                        this.TokenList.RemoveRange(0, 2);
                    }
                    else
                    {
                        this.TokenList[0] = this.registryToken;
                    }
                }
            }
        }

        public RepositoryToken Repository { get; }

        public TagToken? Tag
        {
            get => this.tagToken;
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
                    this.tagToken = value;
                    this.TokenList.AddRange(new Token[]
                    {
                        new SymbolToken(":"),
                        this.tagToken!
                    });
                }
                else
                {
                    this.tagToken = value;

                    if (this.tagToken is null)
                    {
                        // Remove the tag separator and tag tokens
                        this.TokenList.RemoveRange(this.TokenList.Count - 2, 2);
                    }
                    else
                    {
                        this.TokenList[this.TokenList.Count - 1] = this.tagToken;
                    }
                }
            }
        }

        public DigestToken? Digest
        {
            get => this.digestToken;
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
                    this.digestToken = value;
                    this.TokenList.AddRange(new Token[]
                    {
                        new SymbolToken("@"),
                        this.digestToken!
                    });
                }
                else
                {
                    this.digestToken = value;

                    if (this.digestToken is null)
                    {
                        // Remove the digest separator and digest tokens
                        this.TokenList.RemoveRange(this.TokenList.Count - 2, 2);
                    }
                    else
                    {
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
                 from separator in Sprache.Parse.Char('/')
                 from repository in Repository()
                 select ConcatTokens(
                     registry,
                     new SymbolToken(separator.ToString()),
                     repository)).Or(
                from repository in Repository()
                select new Token[] { repository });

            private static Parser<IEnumerable<Token>> Tag() =>
                from separator in Sprache.Parse.Char(':')
                from tag in Sprache.Parse.Identifier(Sprache.Parse.LetterOrDigit, Sprache.Parse.LetterOrDigit)
                select ConcatTokens(
                    new SymbolToken(separator.ToString()),
                    new TagToken(tag));

            private static Parser<IEnumerable<Token>> Digest() =>
                from digestSeparator in Sprache.Parse.Char('@')
                from prefix in Sprache.Parse.String("sha")
                from digits in Sprache.Parse.Digit.Many().Text()
                from shaSeparator in Sprache.Parse.Char(':')
                from digest in Sprache.Parse.Identifier(Sprache.Parse.LetterOrDigit, Sprache.Parse.LetterOrDigit)
                select ConcatTokens(
                    new SymbolToken(digestSeparator.ToString()),
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
