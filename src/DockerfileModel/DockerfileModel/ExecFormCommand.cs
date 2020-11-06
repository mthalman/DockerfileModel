﻿using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ExecFormCommand : Command
    {
        private ExecFormCommand(string text, char escapeChar)
            : base(text, GetInnerParser(escapeChar))
        {
        }

        internal ExecFormCommand(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static ExecFormCommand Create(IEnumerable<string> commands, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullEmptyOrNullElements(commands, nameof(commands));
            return Parse(FormatCommands(commands), escapeChar);
        }

        public static ExecFormCommand Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new ExecFormCommand(text, escapeChar);

        public static string FormatCommands(IEnumerable<string> commands)
        {
            Requires.NotNullEmptyOrNullElements(commands, nameof(commands));
            return $"[{String.Join(", ", commands.Select(command => $"\"{command}\"").ToArray())}]";
        }

        public static Parser<ExecFormCommand> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new ExecFormCommand(tokens);

        public IList<string> CommandArgs =>
            new ProjectedItemList<LiteralToken, string>(
                CommandArgTokens,
                token => token.Value,
                (token, value) => token.Value = value);

        public IEnumerable<LiteralToken> CommandArgTokens => Tokens.OfType<LiteralToken>();

        public override CommandType CommandType => CommandType.ExecForm;

        protected override string GetUnderlyingValue(TokenStringOptions options) =>
            $"[{base.GetUnderlyingValue(options)}]";

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            from openingBracket in Sprache.Parse.Char('[')
            from execFormArgs in
                from arg in ExecFormArg(escapeChar).Once().Flatten()
                from tail in (
                    from delimiter in ExecFormArgDelimiter(escapeChar)
                    from nextArg in ExecFormArg(escapeChar)
                    select ConcatTokens(delimiter, nextArg)).Many()
                select ConcatTokens(arg, tail.Flatten())
            from closingBracket in Sprache.Parse.Char(']')
            select execFormArgs;

        private static Parser<IEnumerable<Token>> ExecFormArgDelimiter(char escapeChar) =>
            from leading in OptionalWhitespaceOrLineContinuation(escapeChar)
            from comma in Symbol(',').AsEnumerable()
            from trailing in OptionalWhitespaceOrLineContinuation(escapeChar)
            select ConcatTokens(
                leading,
                comma,
                trailing);

        private static Parser<IEnumerable<Token>> ExecFormArg(char escapeChar) =>
            from leading in OptionalWhitespaceOrLineContinuation(escapeChar)
            from openingQuote in Symbol(DoubleQuote)
            from argValue in ArgTokens(LiteralToken(escapeChar, new char[] { DoubleQuote }).AsEnumerable(), escapeChar).Many()
            from closingQuote in Symbol(DoubleQuote)
            from trailing in OptionalWhitespaceOrLineContinuation(escapeChar)
            select ConcatTokens(
                leading,
                CollapseCommandTokens(argValue.Flatten(), DoubleQuote),
                trailing);
    }
}