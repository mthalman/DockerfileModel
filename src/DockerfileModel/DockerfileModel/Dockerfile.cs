using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DockerfileModel
{
    public class Dockerfile
    {
        public Dockerfile(IEnumerable<DockerfileLine> dockerfileLines)
        {
            this.Lines = dockerfileLines;
        }

        public IEnumerable<DockerfileLine> Lines { get; }

        public static Dockerfile Parse(string text) =>
            DockerfileParser.ParseContent(text);

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();

            var lines = Lines.ToArray();
            for (int i = 0; i < lines.Length; i++)
            {
                builder.Append(lines[i].ToString());
            }

            return builder.ToString();
        }
    }
}
