﻿using System.Collections.Generic;
using System.Linq;

namespace DockerfileModel
{
    public abstract class Token
    {
        public Token(string content)
        {
            this.Value = content;
        }

        public string Value { get; set; }
    }

    public class KeywordToken : Token
    {
        public KeywordToken(string content) : base(content)
        {
        }
    }

    public class OperatorToken : Token
    {
        public OperatorToken(string content) : base(content)
        {
        }
    }

    public class LiteralToken : Token
    {
        public LiteralToken(string content) : base(content)
        {
        }
    }

    public class WhitespaceToken : Token
    {
        public WhitespaceToken(string content) : base(content)
        {
        }
    }

    public class CommentToken : Token
    {
        public CommentToken(string content) : base(content)
        {
        }
    }
}
