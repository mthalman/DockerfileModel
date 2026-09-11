namespace Valleysoft.DockerfileModel.Tokens;

public class WhitespaceToken : PrimitiveToken
{
    public WhitespaceToken(string value) : base(ValidateValue(value))
    {
    }

    internal static string ValidateValue(string value)
    {
        Guard.NotNullOrEmpty(value, nameof(value));
        Guard.Operation(value.Trim().Length == 0, $"'{value}' contains non-whitespace characters.");
        return value;
    }
}
