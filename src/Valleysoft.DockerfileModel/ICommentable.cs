using System.Collections.Generic;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel
{
    /// <summary>
    /// Represents a model item that is capable of containing Dockerfile comments.
    /// </summary>
    public interface ICommentable
    {
        public IList<string?> Comments { get; }
        public IEnumerable<CommentToken> CommentTokens { get; }
    }
}
