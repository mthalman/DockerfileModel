using System;
using System.Collections.Generic;
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
            from text in TokenWithTrailingWhitespace(str => new CommentTextToken(str))
            select ConcatTokens(leading, comment, text);

        private static Parser<IEnumerable<Token>> TokenWithTrailingWhitespace(Func<string, Token> createToken) =>
            from val in Parse.AnyChar.Except(Parse.LineEnd).Many().Text()
            select ConcatTokens(createToken(val.Trim()), GetTrailingWhitespaceToken(val)!);

        private static Parser<LineContinuationToken> LineContinuation(char escapeChar) =>
            from lineCont in Parse.Char(escapeChar)
            from lineEnding in Parse.LineTerminator
            select new LineContinuationToken(lineCont + lineEnding);

        private static Parser<IEnumerable<Token>> InstructionArgLine(char escapeChar) =>
            from text in Parse.AnyChar.Except(LineContinuation(escapeChar)).Many().Text()
            from lineContinuation in LineContinuation(escapeChar).Optional()
            select ConcatTokens(
                GetLeadingWhitespaceToken(text)!,
                new LiteralToken(text.Trim()),
                GetTrailingWhitespaceToken(text)!,
                lineContinuation.GetOrDefault());

        private static Parser<IEnumerable<Token>> InstructionArgs(char escapeChar) =>
            from lineSets in InstructionArgLine(escapeChar).Many()
            select lineSets.SelectMany(lineSet => lineSet);


        private static WhitespaceToken? GetLeadingWhitespaceToken(string text)
        {
            var whitespace = new string(
                text
                    .TakeWhile(ch => Char.IsWhiteSpace(ch))
                    .ToArray());

            if (whitespace == String.Empty)
            {
                return null;
            }

            return new WhitespaceToken(whitespace);
        }

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

        public static Parser<WhitespaceToken> Whitespace() =>
            from whitespace in Parse.WhiteSpace.Until(Parse.LineTerminator).XMany()
            select new WhitespaceToken(new string(whitespace.SelectMany(chars => chars).ToArray()));

        internal static Parser<string> Identifier() =>
            Parse.Identifier(Parse.Letter, Parse.LetterOrDigit);

        private static Parser<string> InstructionIdentifier(string instructionName) =>
            Parse.IgnoreCase(instructionName).Text();

        private static Parser<KeywordToken> InstructionIdentifier() =>
            from identifier in
                InstructionIdentifier("ARG")
                .Or(InstructionIdentifier("FROM"))
                .Or(InstructionIdentifier("ENV"))
                .Or(InstructionIdentifier("RUN"))
            select new KeywordToken(identifier);

        public static Parser<IEnumerable<Token>> Instruction(char escapeChar) =>
            from leading in WhitespaceChars.AsEnumerable()
            from inst in TokenWithTrailingWhitespace(InstructionIdentifier())
            from instArgs in InstructionArgs(escapeChar)
            select ConcatTokens(leading, inst, instArgs);

        private static string Concat(params string[] strings) =>
            String.Join("", strings);
    }
}
