using System.Text;
using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.BasicParsers;
using static Valleysoft.DockerfileModel.Parsing.InstructionParsers;
using static Valleysoft.DockerfileModel.Parsing.StringParsers;
using static Valleysoft.DockerfileModel.Parsing.TokenSequences;

namespace Valleysoft.DockerfileModel;

/// <summary>A mutable token model of a registry, repository, and optional tag or digest.</summary>
/// <remarks>
/// This model supports either a tag or a digest, not both. It does not contact a registry or
/// resolve implicit defaults. Parsing a FROM operand creates a separate object; assign its
/// serialized text back to <see cref="FromInstruction.ImageName"/> to update the instruction.
/// </remarks>
public class ImageName : AggregateToken
{
    private InnerTokens.Registry? registryToken;
    private InnerTokens.Repository repositoryToken;
    private InnerTokens.Tag? tagToken;
    private InnerTokens.Digest? digestToken;
    private readonly char escapeChar;

    /// <summary>Creates an image reference from components and retains its escape context for later edits.</summary>
    /// <param name="repository">The required repository path.</param>
    /// <param name="registry">An optional registry host, including a port when needed.</param>
    /// <param name="tag">An optional tag, mutually exclusive with <paramref name="digest"/>.</param>
    /// <param name="digest">An optional digest including its algorithm prefix, mutually exclusive with <paramref name="tag"/>.</param>
    /// <param name="escapeChar">The Dockerfile escape character; defaults to backslash.</param>
    public ImageName(string repository, string? registry = null, string? tag = null, string? digest = null,
        char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(repository, registry, tag, digest, escapeChar), escapeChar)
    {
    }

    internal ImageName(IEnumerable<Token> tokens, char escapeChar) : base(tokens)
    {
        this.escapeChar = escapeChar;
        EditingEscapeChar = escapeChar;
        registryToken = Tokens.OfType<InnerTokens.Registry>().FirstOrDefault();
        repositoryToken = Tokens.OfType<InnerTokens.Repository>().First();
        tagToken = Tokens.OfType<InnerTokens.Tag>().FirstOrDefault();
        digestToken = Tokens.OfType<InnerTokens.Digest>().FirstOrDefault();
    }

    /// <summary>Gets or sets the explicit registry; null removes it and its separator.</summary>
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
                RegistryToken = String.IsNullOrEmpty(value) ? null : new InnerTokens.Registry(value!, escapeChar);
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

    /// <summary>Gets or sets the required repository component without normalizing its name.</summary>
    public string Repository
    {
        get => RepositoryToken.Value;
        set
        {
            Guard.NotNullOrEmpty(value, nameof(value));
            RepositoryToken.Value = value;
        }
    }

    private InnerTokens.Repository RepositoryToken
    {
        get => repositoryToken;
        set
        {
            Guard.NotNull(value, nameof(value));
            SetToken(RepositoryToken, value);
            repositoryToken = value;
        }
    }

    /// <summary>Gets or sets the tag; null removes it and its separator.</summary>
    /// <exception cref="InvalidOperationException">A non-null tag is assigned while a digest is present.</exception>
    public string? Tag
    {
        get => this.tagToken?.Value;
        set
        {
            Guard.Operation(
                value is null || Digest is null,
                $"{nameof(Tag)} cannot be set when {nameof(Digest)} is already set.");

            InnerTokens.Tag? tagToken = TagToken;
            if (tagToken is not null && value is not null)
            {
                tagToken.Value = value;
            }
            else
            {
                TagToken = String.IsNullOrEmpty(value) ? null : new InnerTokens.Tag(value!, escapeChar);
            }
        }
    }

    private InnerTokens.Tag? TagToken
    {
        get => tagToken;
        set
        {
            Guard.Operation(
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

    /// <summary>Gets or sets the digest including its algorithm prefix; null removes it and its separator.</summary>
    /// <exception cref="InvalidOperationException">A non-null digest is assigned while a tag is present.</exception>
    public string? Digest
    {
        get => this.digestToken?.Value;
        set
        {
            Guard.Operation(
                value is null || Tag is null,
                $"{nameof(Digest)} cannot be set when {nameof(Tag)} is already set.");

            InnerTokens.Digest? digestToken = DigestToken;
            if (digestToken is not null && value is not null)
            {
                digestToken.Value = value;
            }
            else
            {
                DigestToken = String.IsNullOrEmpty(value) ? null : new InnerTokens.Digest(value!, escapeChar);
            }
        }
    }

    private InnerTokens.Digest? DigestToken
    {
        get => digestToken;
        set
        {
            Guard.Operation(
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

    /// <summary>Joins image components with their separators without performing a registry lookup or parsing the result.</summary>
    /// <remarks>Supply either a tag or a digest, not both.</remarks>
    public static string FormatImageName(string repository, string? registry, string? tag, string? digest)
    {
        Guard.NotNullOrWhiteSpace(repository, nameof(repository));
        Guard.Operation(
            (tag is null && digest is null) || String.IsNullOrEmpty(tag) ^ String.IsNullOrEmpty(digest),
            $"Either {nameof(tag)} may be set or {nameof(digest)} may be set but not both.");

        StringBuilder builder = new();
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

    /// <summary>Parses an image reference into a new, independently editable token model.</summary>
    /// <param name="imageName">Reference syntax accepted by this model, with at most one tag or digest.</param>
    /// <param name="escapeChar">Escape context retained for subsequent component edits.</param>
    /// <returns>A new image model; no source instruction is updated automatically.</returns>
    public static ImageName Parse(string imageName, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(imageName, GetParser(escapeChar)), escapeChar);

    [Obsolete("Use Dockerfile.Parse or Dockerfile.TryParse instead. Parser factories will be removed in the next major version.")]
    public static Parser<IEnumerable<Token>> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
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

            public Digest(string value, char escapeChar)
                : this(GetTokens(value, GetInnerParser(escapeChar)), escapeChar)
            {
            }

            internal Digest(IEnumerable<Token> tokens, char escapeChar)
                : base(tokens, canContainVariables: true, escapeChar)
            {
                this.escapeChar = escapeChar;
            }

            public static Digest Parse(string text, char escapeChar) =>
                new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

            [Obsolete("Use Dockerfile.Parse or Dockerfile.TryParse instead. Parser factories will be removed in the next major version.")]
            public static Parser<Digest> GetParser(char escapeChar) =>
                from tokens in GetInnerParser(escapeChar)
                select new Digest(tokens, escapeChar);

            protected override IEnumerable<Token> GetInnerTokens(string value) =>
                GetTokens(value, GetInnerParser(escapeChar));

            private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
                from prefix in ArgTokens(
                    StringToken("sha", escapeChar), escapeChar)
                from digits in ArgTokens(
                    StringTokenCharWithOptionalLineContinuation(escapeChar, Valleysoft.DockerfileModel.Parsing.Parse.Digit), escapeChar).Many()
                from shaSeparator in ArgTokens(
                    StringTokenCharWithOptionalLineContinuation(escapeChar, Valleysoft.DockerfileModel.Parsing.Parse.Char(':')), escapeChar)
                from digest in ArgTokens(
                    IdentifierString(escapeChar, Valleysoft.DockerfileModel.Parsing.Parse.LetterOrDigit, Valleysoft.DockerfileModel.Parsing.Parse.LetterOrDigit), escapeChar, excludeTrailingWhitespace: true)
                select TokenHelper.CollapseStringTokens(ConcatTokens(
                    prefix,
                    TokenHelper.CollapseStringTokens(digits.Flatten()),
                    shaSeparator,
                    digest));
        }

        public class Tag : LiteralToken
        {
            private readonly char escapeChar;

            public Tag(string value, char escapeChar)
                : this(GetTokens(value, GetInnerParser(escapeChar)), escapeChar)
            {
            }

            internal Tag(IEnumerable<Token> tokens, char escapeChar)
                : base(tokens, canContainVariables: true, escapeChar)
            {
                this.escapeChar = escapeChar;
            }

            public static Tag Parse(string text, char escapeChar) =>
                new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

            [Obsolete("Use Dockerfile.Parse or Dockerfile.TryParse instead. Parser factories will be removed in the next major version.")]
            public static Parser<Tag> GetParser(char escapeChar) =>
                from tokens in GetInnerParser(escapeChar)
                select new Tag(tokens, escapeChar);

            protected override IEnumerable<Token> GetInnerTokens(string value) =>
                GetTokens(value, GetInnerParser(escapeChar));

            private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
                DelimitedIdentifier(escapeChar, FirstCharParser(), TailCharParser(), '/');

            private static Parser<char> FirstCharParser() => Valleysoft.DockerfileModel.Parsing.Parse.LetterOrDigit;

            private static Parser<char> TailCharParser() =>
                Valleysoft.DockerfileModel.Parsing.Parse.LetterOrDigit
                    .Or(Valleysoft.DockerfileModel.Parsing.Parse.Char('.'))
                    .Or(Valleysoft.DockerfileModel.Parsing.Parse.Char('_'))
                    .Or(Valleysoft.DockerfileModel.Parsing.Parse.Char('-'));
        }

        public class Repository : LiteralToken
        {
            private readonly char escapeChar;

            public Repository(string value, char escapeChar)
                : this(GetTokens(value, GetInnerParser(escapeChar)), escapeChar)
            {
            }

            internal Repository(IEnumerable<Token> tokens, char escapeChar)
                : base(tokens, canContainVariables: true, escapeChar)
            {
                this.escapeChar = escapeChar;
            }

            public static Repository Parse(string text, char escapeChar) =>
                new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

            [Obsolete("Use Dockerfile.Parse or Dockerfile.TryParse instead. Parser factories will be removed in the next major version.")]
            public static Parser<Repository> GetParser(char escapeChar) =>
                from tokens in GetInnerParser(escapeChar)
                select new Repository(tokens, escapeChar);

            protected override IEnumerable<Token> GetInnerTokens(string value) =>
                GetTokens(value, GetInnerParser(escapeChar));

            private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
                DelimitedIdentifier(escapeChar, FirstCharParser(), TailCharParser(), '/');

            private static Parser<char> FirstCharParser() => Valleysoft.DockerfileModel.Parsing.Parse.LetterOrDigit;

            private static Parser<char> TailCharParser() =>
                Valleysoft.DockerfileModel.Parsing.Parse.LetterOrDigit
                    .Or(Valleysoft.DockerfileModel.Parsing.Parse.Char('_'))
                    .Or(Valleysoft.DockerfileModel.Parsing.Parse.Char('-'));
        }

        public class Registry : LiteralToken
        {
            private readonly char escapeChar;

            public Registry(string value, char escapeChar)
                : this(GetTokens(value, GetInnerParser(escapeChar)), escapeChar)
            {
            }

            internal Registry(IEnumerable<Token> tokens, char escapeChar)
                : base(tokens, canContainVariables: true, escapeChar)
            {
                this.escapeChar = escapeChar;
            }

            public static Registry Parse(string text, char escapeChar) =>
                new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

            [Obsolete("Use Dockerfile.Parse or Dockerfile.TryParse instead. Parser factories will be removed in the next major version.")]
            public static Parser<Registry> GetParser(char escapeChar) =>
                from tokens in GetInnerParser(escapeChar)
                select new Registry(tokens, escapeChar);

            protected override IEnumerable<Token> GetInnerTokens(string value) =>
                GetTokens(value, GetInnerParser(escapeChar));

            private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
                DelimitedIdentifier(
                    escapeChar,
                    FirstCharParser(),
                    TailCharParser().Or(Valleysoft.DockerfileModel.Parsing.Parse.Char(':')),
                    '.',
                    minimumDelimiters: 1)
                .Or(
                    DelimitedIdentifier(
                        escapeChar,
                        FirstCharParser(),
                        TailCharParser().Or(Valleysoft.DockerfileModel.Parsing.Parse.Char('.')),
                        ':',
                        minimumDelimiters: 1))
                .Or(LocalhostParser());

            private static Parser<IEnumerable<Token>> LocalhostParser() =>
                from localhost in Valleysoft.DockerfileModel.Parsing.Parse.IgnoreCase("localhost").Text()
                select new Token[] { new StringToken(localhost) };

            private static Parser<char> FirstCharParser() => Valleysoft.DockerfileModel.Parsing.Parse.LetterOrDigit;

            private static Parser<char> TailCharParser() =>
                Valleysoft.DockerfileModel.Parsing.Parse.LetterOrDigit
                    .Or(Valleysoft.DockerfileModel.Parsing.Parse.Char('_'))
                    .Or(Valleysoft.DockerfileModel.Parsing.Parse.Char('-'));
        }
    }
}
