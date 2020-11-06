using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DockerfileModel.Tokens;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class LineContinuationTokenTests
    {
        [Fact]
        public void Create()
        {
            LineContinuationToken token = LineContinuationToken.Create();
            Assert.Collection(token.Tokens, new Action<Token>[]
            {
                token => ValidateSymbol(token, '\\'),
                token => ValidateNewLine(token, Environment.NewLine)
            });

            token = LineContinuationToken.Create('`');
            Assert.Collection(token.Tokens, new Action<Token>[]
            {
                token => ValidateSymbol(token, '`'),
                token => ValidateNewLine(token, Environment.NewLine)
            });

            token = LineContinuationToken.Create("\n", '`');
            Assert.Collection(token.Tokens, new Action<Token>[]
            {
                token => ValidateSymbol(token, '`'),
                token => ValidateNewLine(token, "\n")
            });
        }

        [Fact]
        public void Parse()
        {
            LineContinuationToken token = LineContinuationToken.Parse("\\\n");
            Assert.Equal("\\\n", token.ToString());
            Assert.Collection(token.Tokens, new Action<Token>[]
            {
                token => ValidateSymbol(token, '\\'),
                token => ValidateNewLine(token, "\n")
            });

            token = LineContinuationToken.Parse("`  \n", '`');
            Assert.Equal("`  \n", token.ToString());
            Assert.Collection(token.Tokens, new Action<Token>[]
            {
                token => ValidateSymbol(token, '`'),
                token => ValidateWhitespace(token, "  "),
                token => ValidateNewLine(token, "\n")
            });
        }
    }
}
