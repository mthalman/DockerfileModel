using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class CopyInstruction : FileTransferInstruction
    {
        private const string Name = "COPY";

        public CopyInstruction(IEnumerable<string> sources, string destination,
            string? fromStageName = null, UserAccount? changeOwner = null, string? permissions = null, char escapeChar = Dockerfile.DefaultEscapeChar)
            : base(GetTokens(sources, destination, fromStageName, changeOwner, permissions, escapeChar), escapeChar)
        {
        }

        private CopyInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
        {
        }

        public string? FromStageName
        {
            get => FromStageNameToken?.Value;
            set => SetOptionalTokenValue(
                FromStageNameToken, value, val => new StageName(val, EscapeChar), token => FromStageNameToken = token);
        }

        public StageName? FromStageNameToken
        {
            get => FromFlag?.ValueToken;
            set => SetOptionalKeyValueTokenValue(
                FromFlag, value, val => new FromFlag(val, EscapeChar), token => FromFlag = token);
        }

        private FromFlag? FromFlag
        {
            get => Tokens.OfType<FromFlag>().FirstOrDefault();
            set => SetOptionalFlagToken(FromFlag, value);
        }

        public static CopyInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new CopyInstruction(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

        public static Parser<CopyInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new CopyInstruction(tokens, escapeChar);

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            GetInnerParser(escapeChar, Name,
                ArgTokens(FromFlag.GetParser(escapeChar).AsEnumerable(), escapeChar));

        private static IEnumerable<Token> GetTokens(IEnumerable<string> sources, string destination,
           string? fromStageName, UserAccount? changeOwner, string? permissions, char escapeChar)
        {
            string fromFlag = fromStageName is null ? "" : new FromFlag(fromStageName, escapeChar).ToString() + " ";
            string text = CreateInstructionString(sources, destination, changeOwner, permissions, escapeChar, Name, fromFlag);
            return GetTokens(text, GetInnerParser(escapeChar));
        }
    }
}
