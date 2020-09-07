using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;

namespace DockerfileModel
{
    public abstract class DockerfileLine
    {
        protected DockerfileLine(string text, Parser<IEnumerable<Token>> parser)
        {
            Tokens = DockerfileParser.FilterNulls(parser.Parse(text));
        }

        protected DockerfileLine(string text, Parser<Token> parser)
        {
            Tokens = new Token[] { parser.Parse(text) };
        }

        public abstract LineType Type { get; }

        public IEnumerable<Token> Tokens { get; }

        public override string ToString() =>
            String.Join("", Tokens
                .Select(token => token.Value)
                .ToArray());
    }
}
