namespace Valleysoft.DockerfileModel.Tokens;

public class WhitespaceToken : PrimitiveToken
{
    public WhitespaceToken(string value) : base(ValidateValue(value))
    {
    }

    internal static string ValidateValue(string value)
    {
        Requires.NotNullOrEmpty(value, nameof(value));
        Verify.Operation(value.Trim().Length == 0, $"'{value}' contains non-whitespace characters.");
        return value;
    }
}
