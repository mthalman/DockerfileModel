using System.Collections.Generic;
using DockerfileModel.Tokens;

namespace DockerfileModel
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
