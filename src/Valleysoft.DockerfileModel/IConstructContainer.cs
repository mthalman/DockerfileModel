using System.Collections.Generic;

namespace Valleysoft.DockerfileModel
{
    public interface IConstructContainer
    {
        IEnumerable<DockerfileConstruct> Items { get; }
    }
}
