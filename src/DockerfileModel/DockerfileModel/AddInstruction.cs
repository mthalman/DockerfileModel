using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;

namespace DockerfileModel
{
    public class AddInstruction : FileTransferInstruction
    {
        private const string Name = "ADD";

        public AddInstruction(IEnumerable<string> sources, string destination,
            UserAccount? changeOwner = null, string? permissions = null, char escapeChar = Dockerfile.DefaultEscapeChar)
            : base(sources, destination, changeOwner, permissions, escapeChar, Name)
        {
        }

        private AddInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
        {
        }

        public static AddInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new AddInstruction(GetTokens(text, GetInnerParser(escapeChar, Name)), escapeChar);

        public static Parser<AddInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar, Name)
            select new AddInstruction(tokens, escapeChar);
    }
}
