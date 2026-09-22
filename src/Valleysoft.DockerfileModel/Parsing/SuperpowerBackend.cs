using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class SuperpowerBackend
{
    internal static TextParser<char> Matching(Func<char, bool> predicate, string description) =>
        Character.Matching(predicate, description);

    internal static Superpower.Model.Result<char> Match(TextParser<char> parser, IInput input) =>
        parser(new TextSpan(
            input.Source,
            new Superpower.Model.Position(input.Position, input.Line, input.Column),
            input.Source.Length - input.Position));
}
