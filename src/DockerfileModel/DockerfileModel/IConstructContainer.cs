using System.Collections.Generic;

namespace DockerfileModel
{
    public interface IConstructContainer
    {
        IEnumerable<DockerfileConstruct> Items { get; }
    }
}
