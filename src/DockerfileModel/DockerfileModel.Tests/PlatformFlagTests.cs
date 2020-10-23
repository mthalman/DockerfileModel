using System;
using DockerfileModel.Tokens;
using Xunit;

namespace DockerfileModel.Tests
{
    public class PlatformFlagTests
    {
        [Fact]
        public void Platform()
        {
            PlatformFlag platformFlag = PlatformFlag.Create("test");
            Assert.Equal("test", platformFlag.Platform);
            Assert.Equal("test", platformFlag.PlatformToken.Value);

            platformFlag.Platform = "test2";
            Assert.Equal("test2", platformFlag.Platform);
            Assert.Equal("test2", platformFlag.PlatformToken.Value);

            platformFlag.PlatformToken.Value = "test3";
            Assert.Equal("test3", platformFlag.Platform);
            Assert.Equal("test3", platformFlag.PlatformToken.Value);

            platformFlag.PlatformToken = new LiteralToken("test4");
            Assert.Equal("test4", platformFlag.Platform);
            Assert.Equal("test4", platformFlag.PlatformToken.Value);

            Assert.Throws<ArgumentNullException>(() => platformFlag.Platform = null);
            Assert.Throws<ArgumentNullException>(() => platformFlag.PlatformToken = null);
        }
    }
}
