using System;
using Xunit;

namespace DockerfileModel.Tests
{
    public class ImageNameTests
    {
        private const string TestSha = "sha256:2bd5ff6b44502652491093010ede8e31629bd7338459e4773ba5b8d7e61272ee";

        [Theory]
        [InlineData("scratch", null, "scratch", null, null)]
        [InlineData("docker.io/library/image:tag-1.0", "docker.io", "library/image", "tag-1.0", null)]
        [InlineData("myregistry.azurecr.io/repo:tag1", "myregistry.azurecr.io", "repo", "tag1", null)]
        [InlineData("repo1:tag1", null, "repo1", "tag1", null)]
        [InlineData("repo1@" + TestSha, null, "repo1", null, TestSha)]
        [InlineData("docker.io/library/image@" + TestSha, "docker.io", "library/image", null, TestSha)]
        public void Parse(string input, string expectedRegistry, string expectedRepository, string expectedTag, string expectedDigest)
        {
            ResolvedImageName result = ResolvedImageName.Parse(input);
            Assert.Equal(expectedRegistry, result.Registry);
            Assert.Equal(expectedRepository, result.Repository);
            Assert.Equal(expectedTag, result.Tag);
            Assert.Equal(expectedDigest, result.Digest);
            Assert.Equal(input, result.ToString());
        }

        [Theory]
        [InlineData(null, "scratch", null, null, "scratch")]
        [InlineData("docker.io", "library/image", "tag", null, "docker.io/library/image:tag")]
        [InlineData("myregistry.azurecr.io", "repo", "tag1", null, "myregistry.azurecr.io/repo:tag1")]
        [InlineData(null, "repo1", "tag1", null, "repo1:tag1")]
        [InlineData(null, "repo1", null, TestSha, "repo1@" + TestSha)]
        [InlineData("docker.io", "library/image", null, TestSha, "docker.io/library/image@" + TestSha)]
        public void Create(string registry, string repository, string tag, string digest, string expectedOutput)
        {
            ResolvedImageName result = ResolvedImageName.Create(repository, registry, tag, digest);
            Assert.Equal(expectedOutput, result.ToString());

            Assert.Equal(registry, result.Registry);
            Assert.Equal(repository, result.Repository);
            Assert.Equal(tag, result.Tag);
            Assert.Equal(digest, result.Digest);
        }

        [Fact]
        public void CannotSetTagWhenDigestIsSet()
        {
            ResolvedImageName imageName = ResolvedImageName.Create("repo", "registry.io", digest: "sha256:digest");
            Assert.Throws<InvalidOperationException>(() => imageName.Tag = "tag");
        }

        [Fact]
        public void CannotSetDigestWhenTagIsSet()
        {
            ResolvedImageName imageName = ResolvedImageName.Create("repo", "registry.io", "tag");
            Assert.Throws<InvalidOperationException>(() => imageName.Digest = "digest");
        }

        [Fact]
        public void ChangeValues()
        {
            ResolvedImageName imageName = ResolvedImageName.Create("repo", "registry.io", "tag");
            
            imageName.Registry = "registry2.io";
            Assert.Equal("registry2.io", imageName.Registry);

            imageName.Repository = "repo2";
            Assert.Equal("repo2", imageName.Repository);

            imageName.Tag = "tag2";
            Assert.Equal("tag2", imageName.Tag);

            Assert.Equal("registry2.io/repo2:tag2", imageName.ToString());

            imageName.Tag = null;
            Assert.Null(imageName.Tag);

            imageName.Digest = "digest";
            Assert.Equal("digest", imageName.Digest);

            Assert.Equal("registry2.io/repo2@digest", imageName.ToString());

            imageName.Registry = null;
            Assert.Equal("repo2@digest", imageName.ToString());

            imageName.Registry = "myregistry.io";
            Assert.Equal("myregistry.io/repo2@digest", imageName.ToString());

            imageName.Digest = null;
            Assert.Equal("myregistry.io/repo2", imageName.ToString());

            imageName.Digest = "mydigest";
            Assert.Equal("myregistry.io/repo2@mydigest", imageName.ToString());

            imageName.Digest = null;
            imageName.Tag = "mytag";
            Assert.Equal("myregistry.io/repo2:mytag", imageName.ToString());

            imageName.Tag = null;
            Assert.Equal("myregistry.io/repo2", imageName.ToString());
        }
    }
}
