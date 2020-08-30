using System.Collections.Generic;

namespace DockerfileModel
{
    public class Dockerfile
    {
        public Dockerfile(IEnumerable<ParserDirective> parserDirectives, IEnumerable<Comment> comments, IEnumerable<Instruction> instructions)
        {
            this.ParserDirectives = parserDirectives;
            this.Comments = comments;
            this.Instructions = instructions;
        }

        public IEnumerable<ParserDirective> ParserDirectives { get; }
        public IEnumerable<Comment> Comments { get; }
        public IEnumerable<Instruction> Instructions { get; }
    }
}
