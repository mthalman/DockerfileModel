using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;

namespace DockerfileModel
{
    public class AddInstruction : FileTransferInstruction
    {
        private const string Name = "ADD";

        private AddInstruction(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static AddInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new AddInstruction(GetTokens(text, GetInnerParser(escapeChar, Name)));

        public static Parser<AddInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar, Name)
            select new AddInstruction(tokens);

        public static AddInstruction Create(IEnumerable<string> sources, string destination,
            ChangeOwner? changeOwner = null, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Create(sources, destination, changeOwner, escapeChar, Name, Parse);
    }
}
