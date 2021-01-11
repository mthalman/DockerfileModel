namespace Valleysoft.DockerfileModel.Tokens
{
    public class SymbolToken : PrimitiveToken
    {
        public SymbolToken(char value) : base(value.ToString())
        {
        }
    }
}
