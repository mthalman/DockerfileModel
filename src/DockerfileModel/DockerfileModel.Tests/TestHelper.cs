using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public static class TestHelper
    {
        public static string ConcatLines(IEnumerable<string> lines, string lineEnding = "\n") =>
            string.Join(lineEnding, lines.ToArray());

        public static void TestVariablesWithLiteral(Func<LiteralToken> getLiteral, string initialValue, bool canContainVariables) =>
            TestVariablesWithLiteral(getLiteral, initialValue, canContainVariables, null, null);

        public static void TestVariablesWithNullableLiteral(Func<LiteralToken> getLiteral, Action<LiteralToken> setLiteral, Action<string> setValue, string initialValue, bool canContainVariables) =>
            TestVariablesWithLiteral(getLiteral, initialValue, canContainVariables, setLiteral, setValue);

        private static void TestVariablesWithLiteral(Func<LiteralToken> getLiteral, string initialValue, bool canContainVariables, Action<LiteralToken> setLiteral, Action<string> setValue)
        {
            if (canContainVariables)
            {
                Assert.Collection(getLiteral().Tokens, new Action<Token>[]
                {
                    token => ValidateAggregate<VariableRefToken>(token, $"${initialValue}",
                        token => ValidateString(token, initialValue))
                });

                getLiteral().Value = "foo";
                Assert.Collection(getLiteral().Tokens, new Action<Token>[]
                {
                    token => ValidateString(token, "foo")
                });

                getLiteral().Value = "$var2";
                Assert.Collection(getLiteral().Tokens, new Action<Token>[]
                {
                    token => ValidateAggregate<VariableRefToken>(token, "$var2",
                        token => ValidateString(token, "var2"))
                });

                if (setLiteral is not null)
                {
                    setLiteral(null);
                    setValue("$var3");
                    Assert.Collection(getLiteral().Tokens, new Action<Token>[]
                    {
                        token => ValidateAggregate<VariableRefToken>(token, "$var3",
                            token => ValidateString(token, "var3"))
                    });

                    setLiteral(null);
                    setValue("bar");
                    Assert.Collection(getLiteral().Tokens, new Action<Token>[]
                    {
                        token => ValidateString(token, "bar")
                    });
                }
            }
            else
            {
                Assert.Collection(getLiteral().Tokens, new Action<Token>[]
                {
                    token => ValidateString(token, initialValue)
                });

                getLiteral().Value = "$var2";
                Assert.Collection(getLiteral().Tokens, new Action<Token>[]
                {
                    token => ValidateString(token, "$var2")
                });

                if (setLiteral is not null)
                {
                    setLiteral(null);
                    setValue("$var3");
                    Assert.Collection(getLiteral().Tokens, new Action<Token>[]
                    {
                        token => ValidateString(token, "$var3")
                    });
                }
            }
        }
    }
}
