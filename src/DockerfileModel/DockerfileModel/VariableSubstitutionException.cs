using System;

namespace DockerfileModel
{
    public class VariableSubstitutionException : Exception
    {
        public VariableSubstitutionException(string message) : base(message)
        {
        }
    }
}
