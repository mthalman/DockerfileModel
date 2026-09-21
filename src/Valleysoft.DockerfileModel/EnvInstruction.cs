using System.Text;

using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.BasicParsers;
using static Valleysoft.DockerfileModel.Parsing.InstructionParsers;
using static Valleysoft.DockerfileModel.Parsing.TokenSequences;
using static Valleysoft.DockerfileModel.Parsing.VariableParsers;

namespace Valleysoft.DockerfileModel;

/// <summary>An ENV instruction with ordered, mutable key/value assignments.</summary>
public class EnvInstruction : Instruction
{
    public EnvInstruction(IDictionary<string, string> variables, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(variables, escapeChar), escapeChar)
    {
    }

    private EnvInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
        VariableTokens = new TokenList<KeyValueToken<Variable, LiteralToken>>(this);
        Variables = InstructionCollectionEditing.Pairs(VariableTokens, this);
    }

    /// <summary>Gets the live syntax-aware assignment view, not a detached dictionary.</summary>
    /// <remarks>Pair objects remain connected to their tokens. Structural edits preserve trivia by default and must leave required operands.</remarks>
    public EditableList<IKeyValuePair> Variables { get; }

    /// <summary>Gets the corresponding live assignment token view for syntax-level replacement.</summary>
    public EditableList<KeyValueToken<Variable, LiteralToken>> VariableTokens { get; }

    public static EnvInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<EnvInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new EnvInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(IDictionary<string, string> variables, char escapeChar)
    {
        Guard.NotNullOrEmpty(variables, nameof(variables));

        string[] keyValueAssignments = variables
            .Select(kvp => StringHelper.FormatKeyValueAssignment(kvp.Key, kvp.Value))
            .ToArray();

        return GetTokens($"ENV {string.Join(" ", keyValueAssignments)}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("ENV", escapeChar,
            GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        MultiVariableFormat(escapeChar).Or(SingleVariableFormat(escapeChar));

    private static Parser<IEnumerable<Token>> MultiVariableFormat(char escapeChar) =>
        ArgTokens(
            from whitespace in Whitespace().Optional()
            from variable in KeyValueToken<Variable, LiteralToken>.GetParser(
                Variable.GetParser(escapeChar),
                EnvLiteralWithVariables(escapeChar),
                escapeChar: escapeChar,
                excludeLeadingWhitespaceInValue: true,
                excludeTrailingWhitespaceInSeparator: true,
                optionalValue: true).AsEnumerable()
            select ConcatTokens(whitespace.GetOrDefault(), variable), escapeChar
        ).AtLeastOnce().Flatten();

    private static Parser<LiteralToken> EnvLiteralWithVariables(char escapeChar) =>
        EnvRawEscapedQuoteLiteral(escapeChar)
            .Or(LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.AllowedInQuotes));

    private static Parser<LiteralToken> EnvRawEscapedQuoteLiteral(char escapeChar) =>
        input =>
        {
            string source = input.Source;
            int start = input.Position;
            if (start >= source.Length || source[start] is not ('\'' or '"'))
            {
                return Result.Failure<LiteralToken>(input, "Expected quoted ENV literal with escaped quote.", new[] { "ENV literal" });
            }

            int end = start;
            int closingQuoteEnd = -1;
            char quote = source[start];
            bool containsEscapedQuote = false;
            bool closed = false;
            end++;
            while (end < source.Length)
            {
                if (IsEscapedQuoteStart(source, end, escapeChar))
                {
                    containsEscapedQuote = true;
                    end += 2;
                    continue;
                }

                if (source[end] == quote)
                {
                    closed = true;
                    end++;
                    closingQuoteEnd = end;
                    break;
                }

                end++;
            }

            if (!containsEscapedQuote || !closed)
            {
                return Result.Failure<LiteralToken>(input, "Expected quoted ENV literal with escaped quote.", new[] { "ENV literal" });
            }

            while (end < source.Length && !char.IsWhiteSpace(source[end]))
            {
                end++;
            }

            IInput remainder = input;
            for (int i = 0; i < end - start; i++)
            {
                remainder = remainder.Advance();
            }

            LiteralToken literal;
            if (closingQuoteEnd == end)
            {
                string rawInnerValue = source.Substring(start + 1, closingQuoteEnd - start - 2);
                literal = new EnvEscapedQuoteLiteralToken(TokenizeRawEnvValue(rawInnerValue, escapeChar), escapeChar, quote);
            }
            else
            {
                string rawValue = source.Substring(start, end - start);
                literal = new EnvEscapedQuoteLiteralToken(TokenizeRawEnvValue(rawValue, escapeChar), escapeChar);
            }

            return Result.Success(literal, remainder);
        };

    private static bool IsEscapedQuoteStart(string source, int index, char escapeChar)
    {
        if (source[index] != escapeChar || index + 1 >= source.Length || source[index + 1] is not ('\'' or '"'))
        {
            return false;
        }

        int escapeRunLength = 0;
        for (int i = index; i >= 0 && source[i] == escapeChar; i--)
        {
            escapeRunLength++;
        }

        return escapeRunLength % 2 == 1;
    }

    private static IEnumerable<Token> TokenizeRawEnvValue(string value, char escapeChar)
    {
        List<Token> tokens = new();
        StringBuilder literal = new();

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                tokens.Add(new StringToken(literal.ToString()));
                literal.Clear();
            }
        }

        for (int i = 0; i < value.Length;)
        {
            if (value[i] == escapeChar && i + 1 < value.Length)
            {
                literal.Append(value[i]);
                literal.Append(value[i + 1]);
                i += 2;
                continue;
            }

            if (TryReadVariableRef(value, i, escapeChar, out VariableRefToken? variableRef, out int consumed))
            {
                FlushLiteral();
                tokens.Add(variableRef);
                i += consumed;
                continue;
            }

            literal.Append(value[i]);
            i++;
        }

        FlushLiteral();
        return tokens;
    }

    private static bool TryReadVariableRef(
        string value, int start, char escapeChar, out VariableRefToken variableRef, out int consumed)
    {
        variableRef = null!;
        consumed = 0;
        if (value[start] != '$' || start + 1 >= value.Length)
        {
            return false;
        }

        if (value[start + 1] == '{')
        {
            int depth = 1;
            for (int end = start + 2; end < value.Length; end++)
            {
                if (value[end] == '{')
                {
                    depth++;
                }
                else if (value[end] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        string candidate = value.Substring(start, end - start + 1);
                        if (TryParseVariableRef(candidate, escapeChar, out variableRef))
                        {
                            consumed = candidate.Length;
                            return true;
                        }

                        return false;
                    }
                }
            }

            return false;
        }

        if (!IsVariableIdentifierChar(value[start + 1]))
        {
            return false;
        }

        int simpleEnd = start + 2;
        while (simpleEnd < value.Length && IsVariableIdentifierChar(value[simpleEnd]))
        {
            simpleEnd++;
        }

        string simpleCandidate = value.Substring(start, simpleEnd - start);
        if (!TryParseVariableRef(simpleCandidate, escapeChar, out variableRef))
        {
            return false;
        }

        consumed = simpleCandidate.Length;
        return true;
    }

    private static bool TryParseVariableRef(string text, char escapeChar, out VariableRefToken variableRef)
    {
        try
        {
            variableRef = VariableRefToken.Parse(text, escapeChar);
            return true;
        }
        catch (ParseException)
        {
            variableRef = null!;
            return false;
        }
    }

    private static bool IsVariableIdentifierChar(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

    private static Parser<IEnumerable<Token>> SingleVariableFormat(char escapeChar) =>
        ArgTokens(
            KeyValueToken<Variable, LiteralToken>.GetParser(
                Variable.GetParser(escapeChar),
                LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.Allowed),
                separator: ' ',
                escapeChar: escapeChar).AsEnumerable(), escapeChar);
}
