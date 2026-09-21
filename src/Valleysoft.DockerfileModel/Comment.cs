using Valleysoft.DockerfileModel.Parsing;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class Comment : DockerfileConstruct
{
    public Comment(string comment)
        : this(GetTokens(comment))
    {
    }

    internal Comment(IEnumerable<Token> tokens)
        : base(tokens)
    {
    }

    public string? Value
    {
        get => ValueToken.Text;
        set => ValueToken.Text = value;
    }

    public CommentToken ValueToken
    {
        get => Tokens.OfType<CommentToken>().First();
        set
        {
            Guard.NotNull(value, nameof(value));
            SetToken(ValueToken, value);
        }
    }

    public override ConstructType Type => ConstructType.Comment;

    public static Comment Parse(string text)
    {
        Guard.NotNullOrEmpty(text, nameof(text));
        return new Comment(GetTokens(text, BasicParsers.CommentText()));
    }

    private static IEnumerable<Token> GetTokens(string comment)
    {
        Guard.NotNullOrEmpty(comment, nameof(comment));
        return GetTokens($"#{comment}", BasicParsers.CommentText());
    }

    public static bool IsComment(string text)
    {
        Guard.NotNullOrEmpty(text, nameof(text));
        return BasicParsers.CommentText().TryParse(text).WasSuccessful;
    }
}
