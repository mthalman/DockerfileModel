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

        private static Parser<Instruction> Instruction(char escapeChar) =>
            LineContent(
                FromInstruction(escapeChar)
                .Or(ArgInstruction(escapeChar))
                .Or(RunInstruction(escapeChar))
                .Or(EnvInstruction(escapeChar)));

        private static Parser<Comment> Comment() =>
            from leading in Parse.WhiteSpace.Many().Text()
            from commentChar in Parse.Char('#')
            from text in Parse.AnyChar.Except(Parse.LineTerminator).Many().Text()
            select new Comment(Concat(leading, commentChar.ToString(), text));

        private static Parser<IDockerfileLine> AnyStatement(char escapeChar) =>
            Instruction(escapeChar)
            .Or<IDockerfileLine>(Comment())
            .Or(Whitespace());

        private static string Concat(params string[] strings) =>
            String.Join("", strings);
    }
}
