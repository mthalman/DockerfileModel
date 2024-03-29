﻿using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class StartPeriodFlag : KeyValueToken<KeywordToken, LiteralToken>
{
    public StartPeriodFlag(string startPeriod, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(new KeywordToken("start-period", escapeChar), new LiteralToken(startPeriod, canContainVariables: true, escapeChar), isFlag: true)
    {
    }

    internal StartPeriodFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static StartPeriodFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Parse(
            text,
            KeywordToken.GetParser("start-period", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new StartPeriodFlag(tokens),
            escapeChar: escapeChar);

    public static Parser<StartPeriodFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(
            KeywordToken.GetParser("start-period", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new StartPeriodFlag(tokens),
            escapeChar: escapeChar);
}
