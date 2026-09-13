using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

internal static class TolerantDockerfileParser
{
    public static DockerfileParseResult Parse(
        string text, DockerfileParseMode mode, UnknownInstructionBehavior unknownBehavior)
    {
        SourceMap sourceMap = new(text);
        List<DockerfileConstruct> constructs = new();
        List<DockerfileDiagnostic> diagnostics = new();
        char escapeChar = Dockerfile.DefaultEscapeChar;
        bool directivesComplete = false;
        int start = 0;

        while (start < text.Length)
        {
            ConstructReader.Region region = ConstructReader.Read(text, start, escapeChar);
            string content = text.Substring(start, region.End - start);
            SourceSpan span = sourceMap.GetSpan(start, region.End);
            DockerfileConstruct? construct = null;
            DockerfileDiagnostic? diagnostic = null;

            if (region.UnterminatedMarker is int marker)
            {
                diagnostic = new DockerfileDiagnostic(
                    DockerfileDiagnosticCodes.UnterminatedHeredoc, DiagnosticSeverity.Error,
                    "The heredoc has no closing delimiter before the end of the input.",
                    sourceMap.GetSpan(marker, region.End));
                directivesComplete = true;
            }
            else if (!directivesComplete && ParserDirective.IsDirectiveCandidate(content))
            {
                IResult<ParserDirective> result = ParserDirective.GetDiagnosticParser().TryParse(content);
                if (!result.WasSuccessful)
                {
                    diagnostic = Failure(DockerfileDiagnosticCodes.InvalidParserDirective,
                        result.Message, result.Remainder.Position, start, content.Length, sourceMap);
                    directivesComplete = true;
                }
                else if (result.Value.DirectiveName.Equals(ParserDirective.EscapeDirective, StringComparison.OrdinalIgnoreCase) &&
                    result.Value.DirectiveValue is not "\\" and not "`")
                {
                    diagnostic = new DockerfileDiagnostic(
                        DockerfileDiagnosticCodes.InvalidParserDirective, DiagnosticSeverity.Error,
                        "An escape directive must specify a single backslash or backtick.", span);
                    directivesComplete = true;
                }
                else
                {
                    construct = result.Value;
                    if (result.Value.DirectiveName.Equals(ParserDirective.EscapeDirective, StringComparison.OrdinalIgnoreCase))
                    {
                        escapeChar = result.Value.DirectiveValue[0];
                    }
                }
            }
            else
            {
                directivesComplete = true;
                try
                {
                    if (Whitespace.IsWhitespace(content))
                    {
                        construct = new Whitespace(content);
                    }
                    else if (content.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    {
                        construct = Comment.Parse(content);
                    }
                    else
                    {
                        int nameStart = 0;
                        while (nameStart < content.Length && char.IsWhiteSpace(content[nameStart]))
                        {
                            nameStart++;
                        }

                        IResult<KeywordToken> nameResult = KeywordToken.GetParser(escapeChar)
                            .TryParse(content.Substring(nameStart));
                        int nameEnd = nameStart + nameResult.Remainder.Position;
                        if (!nameResult.WasSuccessful ||
                            (nameEnd < content.Length && !char.IsWhiteSpace(content[nameEnd])))
                        {
                            diagnostic = Failure(DockerfileDiagnosticCodes.InvalidSyntax,
                                "Expected an instruction name followed by whitespace or the end of input.",
                                nameEnd, start, content.Length, sourceMap);
                        }
                        else if (Instruction.IsKnownInstruction(nameResult.Value.Value))
                        {
                            construct = Instruction.CreateDiagnosticInstruction(nameResult.Value.Value, content, escapeChar,
                                new InstructionParseContext(region, start));
                        }
                        else
                        {
                            bool preserve = unknownBehavior == UnknownInstructionBehavior.Preserve;
                            diagnostic = new DockerfileDiagnostic(
                                DockerfileDiagnosticCodes.UnknownInstruction,
                                preserve ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                                $"Unknown instruction '{nameResult.Value.Value}'." +
                                    (preserve ? " Its arguments have been preserved without validation." : ""),
                                sourceMap.GetSpan(start + nameStart, start + nameEnd));
                            if (preserve)
                            {
                                construct = new UnknownInstruction(content.Substring(0, nameStart),
                                    nameResult.Value, content.Substring(nameEnd), escapeChar);
                            }
                        }
                    }
                }
                catch (ParseException exception)
                {
                    diagnostic = Failure(DockerfileDiagnosticCodes.InvalidSyntax,
                        exception.Message, exception.Position?.Pos ?? 0, start, content.Length, sourceMap);
                }

                if (construct is not null && !string.Equals(construct.ToString(), content, StringComparison.Ordinal))
                {
                    diagnostic = new DockerfileDiagnostic(DockerfileDiagnosticCodes.InvalidSyntax,
                        DiagnosticSeverity.Error, "The construct could not be parsed without losing source text.", span);
                    construct = null;
                }
            }

            if (diagnostic is not null)
            {
                diagnostics.Add(diagnostic);
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    if (mode == DockerfileParseMode.Strict)
                    {
                        return new DockerfileParseResult(null, diagnostics);
                    }
                    construct = new MalformedConstruct(content);
                }
            }

            if (construct is null)
            {
                throw new InvalidOperationException("A construct parser must produce a model or an error diagnostic.");
            }
            construct.SourceSpan = span;
            constructs.Add(construct);
            start = region.End;
        }

        return new DockerfileParseResult(new Dockerfile(constructs), diagnostics);
    }

    private static DockerfileDiagnostic Failure(
        string code, string message, int localOffset, int start, int length, SourceMap sourceMap)
    {
        int offset = start + Math.Max(0, Math.Min(localOffset, length));
        return new DockerfileDiagnostic(code, DiagnosticSeverity.Error, message,
            sourceMap.GetSpan(offset, offset < start + length ? offset + 1 : offset));
    }
}
