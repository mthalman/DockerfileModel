namespace Valleysoft.DockerfileModel;

public class VariableSubstitutionException : Exception
{
    public VariableSubstitutionException(string message) : base(message)
    {
    }
}
