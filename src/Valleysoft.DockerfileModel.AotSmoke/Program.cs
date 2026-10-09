using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.AotSmoke;

internal static class Program
{
    private static int Main()
    {
        const string source = "FROM alpine\nRUN echo original\n";
        Dockerfile parsed = Dockerfile.Parse(source);
        Require(parsed.ToString() == source, "Dockerfile parsing did not preserve its input.");

        parsed.Items.Add(RunInstruction.Parse("RUN echo added\n"));
        Require(parsed.ToString() == source + "RUN echo added\n",
            "A structural edit with library-defined model types did not preserve the expected output.");

        InheritedDockerfile inheritedDocument = new(parsed.Items);
        RequireRejected(() => inheritedDocument.Items.Add(RunInstruction.Parse("RUN echo added\n")),
            "A consumer-defined Dockerfile subclass was not rejected.");

        ExecFormCommand command = ExecFormCommand.Parse("[\"echo\", \"original\"]");
        InheritedLiteral inheritedToken = new("added");
        RequireRejected(() => command.ValueTokens.Add(inheritedToken),
            "A consumer-defined Token subclass was not rejected.");

        OverridingDockerfile overridingDocument = new(new DockerfileConstruct[]
        {
            FromInstruction.Parse("FROM alpine\n")
        });
        RequireRejected(() => overridingDocument.Items.Add(RunInstruction.Parse("RUN echo added\n")),
            "A Dockerfile serializer override was not rejected.");

        ExecFormCommand overridingCommand = ExecFormCommand.Parse("[\"echo\", \"original\"]");
        RequireRejected(() => overridingCommand.ValueTokens.Add(new OverridingLiteral("added")),
            "A Token serializer override was not rejected.");

        Console.WriteLine("Native AOT smoke checks passed.");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireRejected(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private sealed class InheritedDockerfile(IEnumerable<DockerfileConstruct> items) : Dockerfile(items);

    private sealed class OverridingDockerfile(IEnumerable<DockerfileConstruct> items) : Dockerfile(items)
    {
        public override string ToString() => base.ToString() + "RUN echo unmodeled\n";
    }

    private sealed class InheritedLiteral(string value) : LiteralToken(value);

    private sealed class OverridingLiteral(string value) : LiteralToken(value)
    {
        protected override string GetUnderlyingValue(TokenStringOptions options) =>
            base.GetUnderlyingValue(options) + "unmodeled";
    }
}
