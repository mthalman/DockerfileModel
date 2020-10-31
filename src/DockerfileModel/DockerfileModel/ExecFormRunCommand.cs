using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ExecFormRunCommand : RunCommand
    {
        private ExecFormRunCommand(string text, char escapeChar)
            : base(text, GetInnerParser(escapeChar))
        {
        }

        internal ExecFormRunCommand(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static ExecFormRunCommand Create(IEnumerable<string> commands) =>
            Parse(FormatCommands(commands), Instruction.DefaultEscapeChar);

        public static ExecFormRunCommand Parse(string text, char escapeChar) =>
            new ExecFormRunCommand(text, escapeChar);

        public static string FormatCommands(IEnumerable<string> commands) =>
            $"[{String.Join(", ", commands.Select(command => $"\"{command}\"").ToArray())}]";

        public static Parser<ExecFormRunCommand> GetParser(char escapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new ExecFormRunCommand(tokens);

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

        public IList<string> CommandArgs =>
            new StringWrapperList<LiteralToken>(
                CommandArgTokens,
                token => token.Value,
                (token, value) => token.Value = value);

        public IEnumerable<LiteralToken> CommandArgTokens => Tokens.OfType<LiteralToken>();

        public override RunCommandType CommandType => RunCommandType.ExecForm;

        protected override string GetUnderlyingValue(TokenStringOptions options) =>
            $"[{base.GetUnderlyingValue(options)}]";

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
                CollapseRunCommandTokens(argValue.Flatten(), DoubleQuote),
                trailing);
    }
}
