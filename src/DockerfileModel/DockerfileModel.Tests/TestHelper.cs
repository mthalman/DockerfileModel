using System.Collections.Generic;
using System.Linq;

namespace DockerfileModel.Tests
{
    public static class TestHelper
    {
        public static string ConcatLines(IEnumerable<string> lines, string lineEnding = "\n") =>
            string.Join(lineEnding, lines.ToArray());
    }
}
