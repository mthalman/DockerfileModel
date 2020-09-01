using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sprache;

namespace DockerfileModel
{
    internal static class DockerfileParser
    {
        //public static Dockerfile ParseContent(TextReader textReader)
        //{

        //    List<IDockerfileLine> dockerfileLines = new List<IDockerfileLine>();
        //    bool parserDirectivesComplete = false;
        //    char escapeChar = '\\';

        //    string line = textReader.ReadLine();
        //    while (line != null)
        //    {
        //        if (!parserDirectivesComplete)
        //        {
        //            if (ParserDirective.IsParserDirective(line))
        //            {
        //                dockerfileLines.Add(parserDirectiveResult.Value);

        //                if (parserDirectiveResult.Value.Directive.Equals(
        //                    DockerfileModel.ParserDirective.EscapeDirective, StringComparison.OrdinalIgnoreCase))
        //                {
        //                    escapeChar = parserDirectiveResult.Value.Value[0];
        //                }
        //                continue;
        //            }
        //            else
        //            {
        //                parserDirectivesComplete = true;
        //            }
        //        }

        //        var whitespaceResult = Whitespace().TryParse(remainingContent);
        //        if (whitespaceResult.WasSuccessful)
        //        {
        //            dockerfileLines.Add(whitespaceResult.Value);
        //            continue;
        //        }

        //        var commentResult = Comment().TryParse(remainingContent);
        //        if (commentResult.WasSuccessful)
        //        {
        //            dockerfileLines.Add(commentResult.Value);
        //        }

        //        var instructionResult = Instruction(escapeChar).TryParse(remainingContent);
        //        if (instructionResult.WasSuccessful)
        //        {
        //            dockerfileLines.Add(instructionResult.Value);
        //        }

        //        line = textReader.ReadLine();
        //    }

        //    return new Dockerfile(
        //        dockerfileLines.OfType<ParserDirective>(),
        //        dockerfileLines.OfType<Comment>(),
        //        dockerfileLines.OfType<Instruction>());

        //    //var parserDirectives = ParserDirectives().Parse(dockerfileContent);

        //    //// Remove line continuations
        //    //Regex lineContinuationRegex = new Regex(@"\\\s*$\s*", RegexOptions.Multiline);
        //    //dockerfileContent = lineContinuationRegex.Replace(dockerfileContent, "");

        //    //return Dockerfile(parserDirectives).Parse(dockerfileContent);
        //}

        public readonly static Parser<WhitespaceToken> WhitespaceChars =
            from whitespace in Parse.WhiteSpace.Many().Text()
            select whitespace != "" ? new WhitespaceToken(whitespace) : null;

        public readonly static Parser<OperatorToken> OperatorChar =
            from op in Parse.String("=").Text()
            select new OperatorToken(op);

        public static Parser<IEnumerable<Token>> TokenWithTrailingWhitespace(Parser<Token> parser) =>
            from token in parser
            from trailingWhitespace in WhitespaceChars
            select ConcatTokens(token, trailingWhitespace);

        private readonly static Parser<CommentToken> CommentChar =
            from comment in Parse.String("#").Text()
            select new CommentToken(comment);

        public static Parser<IEnumerable<Token>> CommentText() =>
            from leading in WhitespaceChars.AsEnumerable()
            from comment in TokenWithTrailingWhitespace(CommentChar)
            from text in CommentLiteral
            select ConcatTokens(leading, comment, text);

        private readonly static Parser<IEnumerable<Token>> CommentLiteral =
            from val in Parse.AnyChar.Except(Parse.LineEnd).Many().Text()
            select ConcatTokens(new LiteralToken(val.Trim()), GetTrailingWhitespaceToken(val)!);

        private static WhitespaceToken? GetTrailingWhitespaceToken(string text)
        {
            var whitespace = new string(
                text
                    .Reverse()
                    .TakeWhile(ch => Char.IsWhiteSpace(ch))
                    .Reverse()
                    .ToArray());

            if (whitespace == String.Empty)
            {
                return null;
            }

            return new WhitespaceToken(whitespace);
        }

        private static IEnumerable<Token> ConcatTokens(params Token[] tokens) =>
            FilterNulls(tokens).ToList();

        private static IEnumerable<Token> ConcatTokens(params IEnumerable<Token>[] tokens) =>
            ConcatTokens(
                tokens
                    .SelectMany(tokens => tokens)
                    .ToArray());

        public static IEnumerable<T> FilterNulls<T>(IEnumerable<T> items) =>
            items.Where(item => item != null);

        private readonly static Parser<KeywordToken> DirectiveName =
            from name in Identifier()
            select new KeywordToken(name);

        private readonly static Parser<LiteralToken> DirectiveValue =
            from val in Parse.AnyChar.Except(Parse.WhiteSpace).Many().Text()
            select new LiteralToken(val);

        public static Parser<IEnumerable<Token>> ParserDirectiveParser() =>
            from leading in WhitespaceChars.AsEnumerable()
            from commentChar in TokenWithTrailingWhitespace(CommentChar)
            from directive in TokenWithTrailingWhitespace(DirectiveName)
            from op in TokenWithTrailingWhitespace(OperatorChar)
            from value in TokenWithTrailingWhitespace(DirectiveValue)
            select ConcatTokens(
                leading,
                commentChar,
                directive,
                op,
                value);

        private static Parser<Whitespace> Whitespace() =>
            from whitespace in Parse.WhiteSpace.Except(Parse.LineEnd).Many().Text()
            select new Whitespace(whitespace);

        internal static Parser<string> Identifier() =>
            Parse.Identifier(Parse.Letter, Parse.LetterOrDigit);

        internal static Parser<string> InstructionArgs(char escapeChar) =>
            from args in Parse.AnyChar.Many().Text()//.Until(AnyStatement(escapeChar))
                                                    //select args.Any() ? args.First() : null;
                                                    
            select args;

        private static Parser<string> InstructionIdentifier(string instructionName) =>
            from leading in Parse.WhiteSpace.Many().Text()
            from instruction in Parse.IgnoreCase(instructionName).Text()
            from trailing in Parse.WhiteSpace.Many().Text()
            select instruction;

        private static Parser<string> ArgInstructionIdentifier() => InstructionIdentifier("ARG");
        private static Parser<string> FromInstructionIdentifier() => InstructionIdentifier("FROM");
        private static Parser<string> EnvInstructionIdentifier() => InstructionIdentifier("ENV");
        private static Parser<string> RunInstructionIdentifier() => InstructionIdentifier("RUN");

        private static Parser<T> LineContent<T>(Parser<T> parser) =>
            from leading in Parse.WhiteSpace.Many()
            from item in parser
            from trailing in Parse.WhiteSpace.Many()
            select item;

        internal static Parser<Instruction> FromInstruction(char escapeChar) =>
            from leading in Parse.WhiteSpace.Many().Text()
            from inst in FromInstructionIdentifier()
            from instArgs in InstructionArgs(escapeChar)
            select new Instruction(inst, instArgs.Trim());

        private static Parser<Instruction> ArgInstruction(char escapeChar) =>
            from leading in Parse.WhiteSpace.Many().Text()
            from inst in ArgInstructionIdentifier()
            from instArgs in InstructionArgs(escapeChar)
            select new Instruction(inst, instArgs);

        private static Parser<Instruction> RunInstruction(char escapeChar) =>
            from leading in Parse.WhiteSpace.Many().Text()
            from inst in RunInstructionIdentifier()
            from instArgs in InstructionArgs(escapeChar)
            select new Instruction(inst, instArgs);

        private static Parser<Instruction> EnvInstruction(char escapeChar) =>
            from leading in Parse.WhiteSpace.Many().Text()
            from inst in EnvInstructionIdentifier()
            from instArgs in InstructionArgs(escapeChar)
            select new Instruction(inst, instArgs);

        private static string Concat(params string[] strings) =>
            String.Join("", strings);
    }
}
