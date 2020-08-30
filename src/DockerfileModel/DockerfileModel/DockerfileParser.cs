using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;

namespace DockerfileModel
{
    internal static class DockerfileParser
    {
        public static Dockerfile ParseContent(string content)
        {
            int[] newLineIndices = GetNewLineIndices(content);

            List<IDockerfileLine> dockerfileLines = new List<IDockerfileLine>();
            bool parserDirectivesComplete = false;
            char escapeChar = '\\';
            for (int i = 0; i < newLineIndices.Length; i++)
            {
                string remainingContent = i == 0 ? content : content.Substring(newLineIndices[i] + 1);

                if (!parserDirectivesComplete)
                {
                    var parserDirectiveResult = ParserDirective(i, escapeChar).TryParse(remainingContent);
                    if (parserDirectiveResult.WasSuccessful)
                    {
                        dockerfileLines.Add(parserDirectiveResult.Value);

                        if (parserDirectiveResult.Value.Directive.Equals(
                            DockerfileModel.ParserDirective.EscapeDirective, StringComparison.OrdinalIgnoreCase))
                        {
                            escapeChar = parserDirectiveResult.Value.Value[0];
                        }
                        continue;
                    }
                    else
                    {
                        parserDirectivesComplete = true;
                    }
                }

                var whitespaceResult = Whitespace(i).TryParse(remainingContent);
                if (whitespaceResult.WasSuccessful)
                {
                    dockerfileLines.Add(whitespaceResult.Value);
                    continue;
                }

                var commentResult = Comment(i).TryParse(remainingContent);
                if (commentResult.WasSuccessful)
                {
                    dockerfileLines.Add(commentResult.Value);
                }

                var instructionResult = Instruction(i, escapeChar).TryParse(remainingContent);
                if (instructionResult.WasSuccessful)
                {
                    dockerfileLines.Add(instructionResult.Value);
                }
            }

            return new Dockerfile(
                dockerfileLines.OfType<ParserDirective>(),
                dockerfileLines.OfType<Comment>(),
                dockerfileLines.OfType<Instruction>());

            //var parserDirectives = ParserDirectives().Parse(dockerfileContent);

            //// Remove line continuations
            //Regex lineContinuationRegex = new Regex(@"\\\s*$\s*", RegexOptions.Multiline);
            //dockerfileContent = lineContinuationRegex.Replace(dockerfileContent, "");

            //return Dockerfile(parserDirectives).Parse(dockerfileContent);
        }

        private static int[] GetNewLineIndices(string content)
        {
            List<int> newLineIndices = new List<int>();
            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] == '\n')
                {
                    newLineIndices.Add(i);
                }
            }
            return newLineIndices.ToArray();
        }

        private static Parser<Whitespace> Whitespace(int lineNumber) =>
            from whitespace in Parse.WhiteSpace.Except(Parse.LineEnd).Many().Text()
            select new Whitespace(lineNumber, whitespace);

        private static Parser<ParserDirective> ParserDirective(int lineNumber, char escapeChar) =>
            from leading in Parse.WhiteSpace.Many().Text()
            from commentChar in Parse.String("#").Text()
            from directive in Identifier()
            from equal in Parse.String("=").Text()
            from value in Parse.AnyChar.Except(Parse.LineEnd).Many().Text()
            select new ParserDirective(lineNumber, Concat(leading, commentChar, directive, equal, value), directive, value);

        private static Parser<string> Identifier() =>
            Parse.Identifier(Parse.Letter, Parse.LetterOrDigit);

        internal static Parser<string> InstructionArgs(char escapeChar) =>
            from args in Parse.AnyChar.Many().Text()//.Until(AnyStatement(escapeChar))
                                                    //select args.Any() ? args.First() : null;
                                                    
            select args;

        private static Parser<InstructionInfo> InstructionIdentifier(string instructionName) =>
            from leading in Parse.WhiteSpace.Many().Text()
            from instruction in Parse.IgnoreCase(instructionName).Text()
            from trailing in Parse.WhiteSpace.Many().Text()
            select new InstructionInfo(instruction, leading);

        private static Parser<InstructionInfo> ArgInstructionIdentifier() => InstructionIdentifier("ARG");
        private static Parser<InstructionInfo> FromInstructionIdentifier() => InstructionIdentifier("FROM");
        private static Parser<InstructionInfo> EnvInstructionIdentifier() => InstructionIdentifier("ENV");
        private static Parser<InstructionInfo> RunInstructionIdentifier() => InstructionIdentifier("RUN");

        private static Parser<T> LineContent<T>(Parser<T> parser) =>
            from leading in Parse.WhiteSpace.Many()
            from item in parser
            from trailing in Parse.WhiteSpace.Many()
            select item;

        internal static Parser<Instruction> FromInstruction(int lineNumber, char escapeChar) =>
            from leading in Parse.WhiteSpace.Many().Text()
            from inst in FromInstructionIdentifier()
            from instArgs in InstructionArgs(escapeChar)
            select new Instruction(lineNumber, inst.LeadingWhitespace, inst.InstructionName, instArgs.Trim());

        private static Parser<Instruction> ArgInstruction(int lineNumber, char escapeChar) =>
            from leading in Parse.WhiteSpace.Many().Text()
            from inst in ArgInstructionIdentifier()
            from instArgs in InstructionArgs(escapeChar)
            select new Instruction(lineNumber, inst.LeadingWhitespace, inst.InstructionName, instArgs);

        private static Parser<Instruction> RunInstruction(int lineNumber, char escapeChar) =>
            from leading in Parse.WhiteSpace.Many().Text()
            from inst in RunInstructionIdentifier()
            from instArgs in InstructionArgs(escapeChar)
            select new Instruction(lineNumber, inst.LeadingWhitespace, inst.InstructionName, instArgs);

        private static Parser<Instruction> EnvInstruction(int lineNumber, char escapeChar) =>
            from leading in Parse.WhiteSpace.Many().Text()
            from inst in EnvInstructionIdentifier()
            from instArgs in InstructionArgs(escapeChar)
            select new Instruction(lineNumber, inst.LeadingWhitespace, inst.InstructionName, instArgs);

        private static Parser<Instruction> Instruction(int lineNumber, char escapeChar) =>
            LineContent(
                FromInstruction(lineNumber, escapeChar)
                .Or(ArgInstruction(lineNumber, escapeChar))
                .Or(RunInstruction(lineNumber, escapeChar))
                .Or(EnvInstruction(lineNumber, escapeChar)));

        private static Parser<Comment> Comment(int lineNumber) =>
            from leading in Parse.WhiteSpace.Many().Text()
            from commentChar in Parse.Char('#')
            from text in Parse.AnyChar.Except(Parse.LineTerminator).Many().Text()
            select new Comment(lineNumber, Concat(leading, commentChar.ToString(), text), text);

        private static Parser<IDockerfileLine> AnyStatement(char escapeChar) =>
            Instruction(0, escapeChar)
            .Or<IDockerfileLine>(Comment(0))
            .Or(Whitespace(0));

        private static string Concat(params string[] strings) =>
            String.Join("", strings);

        private class InstructionInfo
        {
            public InstructionInfo(string instruction, string leadingWhitespace)
            {
                this.InstructionName = instruction;
                this.LeadingWhitespace = leadingWhitespace;
            }

            public string InstructionName { get; }
            public string LeadingWhitespace { get; }
        }
    }
}
