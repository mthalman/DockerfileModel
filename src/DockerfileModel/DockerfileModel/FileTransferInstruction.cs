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

        protected FileTransferInstruction(IEnumerable<string> sources, string destination,
           ChangeOwner? changeOwner, char escapeChar, string instructionName)
            : this(GetTokens(sources, destination, changeOwner, escapeChar, instructionName))
        {
        }

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
                        new ChangeOwnerFlag(value);
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

        protected static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar, string instructionName,
            Parser<IEnumerable<Token>>? optionalFlagParser = null) =>
            Instruction(instructionName, escapeChar, GetArgsParser(escapeChar, optionalFlagParser));

        private static IEnumerable<Token> GetTokens(IEnumerable<string> sources, string destination,
           ChangeOwner? changeOwner, char escapeChar, string instructionName)
        {
            string text = CreateInstructionString(sources, destination, changeOwner, escapeChar, instructionName, null);
            return GetTokens(text, GetInnerParser(escapeChar, instructionName));
        }

        protected static string CreateInstructionString(IEnumerable<string> sources, string destination,
           ChangeOwner? changeOwner, char escapeChar, string instructionName, string? optionalFlag)
        {
            Requires.NotNullEmptyOrNullElements(sources, nameof(sources));
            Requires.NotNullOrEmpty(destination, nameof(destination));

            IEnumerable<string> locations = sources.Append(destination);

            string changeOwnerFlagStr = changeOwner is null ?
                string.Empty :
                $"{new ChangeOwnerFlag(changeOwner)} ";

            string flags = $"{optionalFlag}{changeOwnerFlagStr}";

            TokenBuilder builder = new TokenBuilder();
            builder
                .Keyword(instructionName)
                .Whitespace(" ");

            if (changeOwner is not null)
            {
                builder.Tokens.Add(changeOwner);
                builder.Whitespace(" ");
            }

            bool useJsonForm = locations.Any(loc => loc.Contains(" "));
            if (useJsonForm)
            {
                return $"{instructionName} {flags}{StringHelper.FormatAsJson(locations)}";
            }
            else
            {
                return $"{instructionName} {flags}{String.Join(" ", locations.ToArray())}";
            }
        }

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar, Parser<IEnumerable<Token>>? optionalFlagParser) =>
            from flags in ArgTokens(
                from flag in FlagOption(escapeChar, optionalFlagParser).Optional()
                select flag.GetOrDefault(), escapeChar).Many().Flatten()
            from whitespace in Whitespace()
            from files in ArgTokens(JsonArray(escapeChar, canContainVariables: true), escapeChar).Or(
                from literals in ArgTokens(
                    LiteralWithVariables(escapeChar).AsEnumerable(),
                    escapeChar).Many()
                select literals.Flatten())
            select ConcatTokens(flags, whitespace, files);

        private static Parser<IEnumerable<Token>?> FlagOption(char escapeChar, Parser<IEnumerable<Token>>? optionalFlagParser) =>
            ChangeOwnerFlag.GetParser(escapeChar: escapeChar).AsEnumerable()
                .Or(optionalFlagParser ?? Parse.Return<IEnumerable<Token>?>(null));
    }
}
