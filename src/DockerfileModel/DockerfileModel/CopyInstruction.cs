using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;

namespace DockerfileModel
{
    public class CopyInstruction : FileTransferInstruction
    {
        private const string Name = "COPY";

        private CopyInstruction(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static CopyInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new CopyInstruction(GetTokens(text, GetInnerParser(escapeChar, Name)));

        public static Parser<CopyInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar, Name)
            select new CopyInstruction(tokens);

        public static CopyInstruction Create(IEnumerable<string> sources, string destination,
            ChangeOwnerFlag? changeOwnerFlag = null, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Create(sources, destination, changeOwnerFlag, escapeChar, Name, Parse);
    }
}
