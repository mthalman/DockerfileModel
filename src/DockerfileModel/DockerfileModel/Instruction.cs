using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public abstract class Instruction : DockerfileConstruct, ICommentable
    {
        protected Instruction(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public string InstructionName
        {
            get => this.InstructionNameToken.Value;
        }

        public KeywordToken InstructionNameToken
        {
            get => Tokens.OfType<KeywordToken>().First();
        }

        public IList<string?> Comments => GetComments();

        public IEnumerable<CommentToken> CommentTokens => GetCommentTokens();

        public override ConstructType Type => ConstructType.Instruction;
    }
}
