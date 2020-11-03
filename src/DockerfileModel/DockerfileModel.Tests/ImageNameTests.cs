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
        [InlineData("my-registry.com/r_e-po1:tag.1", "my-registry.com", "r_e-po1", "tag.1", null)]
        public void Parse(string input, string expectedRegistry, string expectedRepository, string expectedTag, string expectedDigest)
        {
            ImageName result = ImageName.Parse(input);
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
            ImageName result = ImageName.Create(repository, registry, tag, digest);
            Assert.Equal(expectedOutput, result.ToString());

            Assert.Equal(registry, result.Registry);
            Assert.Equal(repository, result.Repository);
            Assert.Equal(tag, result.Tag);
            Assert.Equal(digest, result.Digest);
        }

        [Fact]
        public void CannotSetTagWhenDigestIsSet()
        {
            ImageName imageName = ImageName.Create("repo", "registry.io", digest: "sha256:digest");
            Assert.Throws<InvalidOperationException>(() => imageName.Tag = "tag");
        }

        [Fact]
        public void CannotSetDigestWhenTagIsSet()
        {
            ImageName imageName = ImageName.Create("repo", "registry.io", "tag");
            Assert.Throws<InvalidOperationException>(() => imageName.Digest = "digest");
        }

        [Fact]
        public void ChangeValues()
        {
            ImageName imageName = ImageName.Create("repo", "registry.io", "tag");
            
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

        [Fact]
        public void Registry()
        {
            ImageName imageName = ImageName.Create("repo");
            Assert.Null(imageName.Registry);
            Assert.Null(imageName.RegistryToken);

            imageName.Registry = "test";
            Assert.Equal("test", imageName.Registry);
            Assert.Equal("test", imageName.RegistryToken.Value);

            imageName.Registry = null;
            Assert.Null(imageName.Registry);
            Assert.Null(imageName.RegistryToken);

            imageName.RegistryToken = new RegistryToken("test2");
            Assert.Equal("test2", imageName.Registry);
            Assert.Equal("test2", imageName.RegistryToken.Value);

            imageName.RegistryToken.Value = "test3";
            Assert.Equal("test3", imageName.Registry);
            Assert.Equal("test3", imageName.RegistryToken.Value);

            imageName.RegistryToken = null;
            Assert.Null(imageName.Registry);
            Assert.Null(imageName.RegistryToken);
        }

        [Fact]
        public void Repository()
        {
            ImageName imageName = ImageName.Create("test");
            Assert.Equal("test", imageName.Repository);
            Assert.Equal("test", imageName.RepositoryToken.Value);

            imageName.Repository = "test2";
            Assert.Equal("test2", imageName.Repository);
            Assert.Equal("test2", imageName.RepositoryToken.Value);

            imageName.RepositoryToken = new RepositoryToken("test3");
            Assert.Equal("test3", imageName.Repository);
            Assert.Equal("test3", imageName.RepositoryToken.Value);

            imageName.RepositoryToken.Value = "test4";
            Assert.Equal("test4", imageName.Repository);
            Assert.Equal("test4", imageName.RepositoryToken.Value);

            Assert.Throws<ArgumentNullException>(() => imageName.Repository = null);
            Assert.Throws<ArgumentNullException>(() => imageName.RepositoryToken = null);
        }

        [Fact]
        public void Tag()
        {
            ImageName imageName = ImageName.Create("repo");
            Assert.Null(imageName.Tag);
            Assert.Null(imageName.TagToken);

            imageName.Tag = "test";
            Assert.Equal("test", imageName.Tag);
            Assert.Equal("test", imageName.TagToken.Value);

            imageName.Tag = null;
            Assert.Null(imageName.Tag);
            Assert.Null(imageName.TagToken);

            imageName.TagToken = new TagToken("test2");
            Assert.Equal("test2", imageName.Tag);
            Assert.Equal("test2", imageName.TagToken.Value);

            imageName.TagToken.Value = "test3";
            Assert.Equal("test3", imageName.Tag);
            Assert.Equal("test3", imageName.TagToken.Value);

            Assert.Throws<InvalidOperationException>(() => imageName.Digest = "foo");
            Assert.Throws<InvalidOperationException>(() => imageName.DigestToken = new DigestToken("foo"));

            imageName.TagToken = null;
            Assert.Null(imageName.Tag);
            Assert.Null(imageName.TagToken);
        }

        [Fact]
        public void Digest()
        {
            ImageName imageName = ImageName.Create("repo");
            Assert.Null(imageName.Digest);
            Assert.Null(imageName.DigestToken);

            imageName.Digest = "test";
            Assert.Equal("test", imageName.Digest);
            Assert.Equal("test", imageName.DigestToken.Value);

            imageName.Digest = null;
            Assert.Null(imageName.Digest);
            Assert.Null(imageName.DigestToken);

            imageName.DigestToken = new DigestToken("test2");
            Assert.Equal("test2", imageName.Digest);
            Assert.Equal("test2", imageName.DigestToken.Value);

            imageName.DigestToken.Value = "test3";
            Assert.Equal("test3", imageName.Digest);
            Assert.Equal("test3", imageName.DigestToken.Value);

            Assert.Throws<InvalidOperationException>(() => imageName.Tag = "foo");
            Assert.Throws<InvalidOperationException>(() => imageName.TagToken = new TagToken("foo"));

            imageName.DigestToken = null;
            Assert.Null(imageName.Digest);
            Assert.Null(imageName.DigestToken);
        }
    }
}
