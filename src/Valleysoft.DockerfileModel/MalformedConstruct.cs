using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>Unparsed original text retained after an error. This is not a validated instruction.</summary>
public sealed class MalformedConstruct : DockerfileConstruct
{
    internal MalformedConstruct(string text) : base(new Token[] { new StringToken(text) })
    {
    }

    public override ConstructType Type => ConstructType.Malformed;
}
