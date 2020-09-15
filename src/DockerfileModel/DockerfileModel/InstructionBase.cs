using System.Collections.Generic;
using System.Linq;
using Sprache;

namespace DockerfileModel
{
    public abstract class InstructionBase : DockerfileLine
    {
        protected InstructionBase(string text, Parser<IEnumerable<Token?>> parser)
            : base(text, parser)
        {
        }

        public string InstructionName
        {
            get => this.InstructionNameToken.Value;
            set { this.InstructionNameToken.Value = value; }
        }

        private KeywordToken InstructionNameToken => Tokens.OfType<KeywordToken>().First();

        public IEnumerable<CommentTextToken> Comments => GetComments();

        public override LineType Type => LineType.Instruction;
    }
}
