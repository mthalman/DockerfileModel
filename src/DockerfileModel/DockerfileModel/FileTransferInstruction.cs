using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public abstract class FileTransferInstruction : Instruction
    {
        private readonly TokenList<LiteralToken> sourceTokens;

        protected FileTransferInstruction(IEnumerable<Token> tokens) : base(tokens)
        {
            this.sourceTokens = new TokenList<LiteralToken>(TokenList,
                literals => literals.Take(literals.Count() - 1));
        }

        public IList<string> Sources =>
            new ProjectedItemList<LiteralToken, string>(
                SourceTokens,
                token => token.Value,
                (token, value) => token.Value = value);

        public IList<LiteralToken> SourceTokens => sourceTokens;

        public string Destination
        {
            get => DestinationToken.Value;
            set
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                DestinationToken.Value = value;
            }
        }

        public LiteralToken DestinationToken
        {
            get => Tokens.OfType<LiteralToken>().Last();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(DestinationToken, value);
            }
        }

        public ChangeOwner? ChangeOwner
        {
            get => ChangeOwnerFlagToken?.ValueToken;
            set
            {
                ChangeOwnerFlag? changeOwnerToken = ChangeOwnerFlagToken;
                if (changeOwnerToken is not null && value is not null)
                {
                    changeOwnerToken.ValueToken = value;
                }
                else
                {
                    ChangeOwnerFlagToken = value is null ?
                        null :
                        ChangeOwnerFlag.Create(value);
                }
            }
        }

        private ChangeOwnerFlag? ChangeOwnerFlagToken
        {
            get => Tokens.OfType<ChangeOwnerFlag>().FirstOrDefault();
            set
            {
                SetToken(ChangeOwnerFlagToken, value,
                    addToken: token =>
                    {
                        TokenList.InsertRange(1, new Token[]
                        {
                            new WhitespaceToken(" "),
                            token
                        });
                    },
                    removeToken: token =>
                    {
                        TokenList.RemoveRange(
                            TokenList.FirstPreviousOfType<Token, WhitespaceToken>(token),
                            token);
                    });
            }
        }

        protected static TInstruction Create<TInstruction>(IEnumerable<string> sources, string destination,
            ChangeOwner? changeOwner, char escapeChar,
            string instructionName, Func<string, char, TInstruction> parse)
            where TInstruction : FileTransferInstruction
        {
            Requires.NotNullEmptyOrNullElements(sources, nameof(sources));
            Requires.NotNullOrEmpty(destination, nameof(destination));

            IEnumerable<string> locations = sources.Append(destination);

            string changeOwnerFlagStr = changeOwner is null ?
                string.Empty :
                $"{ChangeOwnerFlag.Create(changeOwner)} ";

            bool useJsonForm = locations.Any(loc => loc.Contains(" "));
            if (useJsonForm)
            {
                return parse($"{instructionName} {changeOwnerFlagStr}{StringHelper.FormatAsJson(locations)}", escapeChar);
            }
            else
            {
                return parse($"{instructionName} {changeOwnerFlagStr}{String.Join(" ", locations.ToArray())}", escapeChar);
            }
        }

        protected static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar, string instructionName) =>
            Instruction(instructionName, escapeChar,
                GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            from changeOwner in ArgTokens(
                ChangeOwnerFlag.GetParser(escapeChar: escapeChar).AsEnumerable(), escapeChar).Optional()
            from whitespace in Whitespace()
            from files in JsonArray(escapeChar, canContainVariables: true).Or(
                from literals in ArgTokens(
                    LiteralAggregate(escapeChar).AsEnumerable(),
                    escapeChar).Many()
                select literals.Flatten())
            select ConcatTokens(changeOwner.GetOrDefault(), whitespace, files);
    }
}
