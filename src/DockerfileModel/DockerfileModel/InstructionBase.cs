using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;

namespace DockerfileModel
{
    public abstract class InstructionBase : DockerfileConstruct
    {
        protected InstructionBase(string text, Parser<IEnumerable<Token?>> parser)
            : base(text, parser)
        {
        }

        public string InstructionName
        {
            get => this.InstructionNameToken.Value;
            set => this.InstructionNameToken.Value = value;
        }

        private KeywordToken InstructionNameToken => Tokens.OfType<KeywordToken>().First();

        public IList<string> Comments => GetComments();

        public override ConstructType Type => ConstructType.Instruction;
    }
}
