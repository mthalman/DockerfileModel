using System;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;

namespace Valleysoft.DockerfileModel.Tests
{
    public abstract class TestScenario<T>
    {
        public Action<T> Validate { get; set; }
        public Action<Token>[] TokenValidators { get; set; }
    }

    public class ParseTestScenario<T> : TestScenario<T>
    {
        public string Text { get; set; }
        public Position ParseExceptionPosition { get; set; }
    }
}
