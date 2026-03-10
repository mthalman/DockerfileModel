using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class AddInstructionTests : FileTransferInstructionTests<AddInstruction>
{
    public AddInstructionTests()
        : base("ADD", AddInstruction.Parse,
            (sources, destination, changeOwner, permissions, escapeChar) =>
                new AddInstruction(sources, destination, changeOwner: changeOwner, permissions: permissions, escapeChar: escapeChar))
    {
    }

    [Theory]
    [MemberData(nameof(ParseTestInputBase))]
    public void ParseBase(ParseTestScenario<AddInstruction> scenario) => RunParseTest(scenario);

    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<AddInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, AddInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInputBase))]
    public void CreateBase(CreateTestScenario scenario) => RunCreateTest(scenario);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(AddInstructionCreateTestScenario scenario)
    {
        AddInstruction result = new(
            scenario.Sources,
            scenario.Destination,
            scenario.ChangeOwner,
            scenario.Permissions,
            escapeChar: scenario.EscapeChar,
            checksum: scenario.Checksum,
            keepGitDir: scenario.KeepGitDir,
            link: scenario.Link,
            unpack: scenario.Unpack,
            excludes: scenario.Excludes);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void Checksum()
    {
        // Not specified by default
        AddInstruction instruction = new(new string[] { "src" }, "dst", escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.Null(instruction.Checksum);
        Assert.Null(instruction.ChecksumToken);
        Assert.Equal("ADD src dst", instruction.ToString());

        // Set via property
        instruction.Checksum = "sha256:abc123";
        Assert.Equal("sha256:abc123", instruction.Checksum);
        Assert.Equal("sha256:abc123", instruction.ChecksumToken!.Value);
        Assert.Equal("ADD --checksum=sha256:abc123 src dst", instruction.ToString());

        // Update via property
        instruction.Checksum = "sha512:def456";
        Assert.Equal("sha512:def456", instruction.Checksum);
        Assert.Equal("sha512:def456", instruction.ChecksumToken!.Value);
        Assert.Equal("ADD --checksum=sha512:def456 src dst", instruction.ToString());

        // Clear via property
        instruction.Checksum = null;
        Assert.Null(instruction.Checksum);
        Assert.Null(instruction.ChecksumToken);
        Assert.Equal("ADD src dst", instruction.ToString());

        // Set via token
        instruction.ChecksumToken = new LiteralToken("sha256:abc123");
        Assert.Equal("sha256:abc123", instruction.Checksum);
        Assert.Equal("sha256:abc123", instruction.ChecksumToken.Value);
        Assert.Equal("ADD --checksum=sha256:abc123 src dst", instruction.ToString());

        // Update token value directly
        instruction.ChecksumToken.Value = "sha384:newvalue";
        Assert.Equal("sha384:newvalue", instruction.Checksum);
        Assert.Equal("sha384:newvalue", instruction.ChecksumToken.Value);
        Assert.Equal("ADD --checksum=sha384:newvalue src dst", instruction.ToString());

        // Clear via token
        instruction.ChecksumToken = null;
        Assert.Null(instruction.Checksum);
        Assert.Null(instruction.ChecksumToken);
        Assert.Equal("ADD src dst", instruction.ToString());

        // Construct with checksum directly in the constructor
        instruction = new(new string[] { "src" }, "dst", checksum: "sha256:abc123", escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.Equal("sha256:abc123", instruction.Checksum);
        Assert.Equal("ADD --checksum=sha256:abc123 src dst", instruction.ToString());

        // Toggle off
        instruction.Checksum = null;
        Assert.Null(instruction.Checksum);
        Assert.Equal("ADD src dst", instruction.ToString());

        // Parse with --checksum already present and remove it
        instruction = AddInstruction.Parse($"ADD`\n --checksum=sha256:abc123`\n src dst", '`');
        instruction.Checksum = null;
        Assert.Null(instruction.Checksum);
        Assert.Equal($"ADD`\n`\n src dst", instruction.ToString());
    }

    [Fact]
    public void ChecksumWithVariables()
    {
        AddInstruction instruction = new(new string[] { "src" }, "dst", checksum: "$var", escapeChar: Dockerfile.DefaultEscapeChar);
        TestHelper.TestVariablesWithNullableLiteral(
            () => instruction.ChecksumToken!, token => instruction.ChecksumToken = token, val => instruction.Checksum = val, "var", canContainVariables: true);
    }

    [Fact]
    public void KeepGitDir()
    {
        // Not set by default
        AddInstruction instruction = new(new string[] { "src" }, "dst", escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.False(instruction.KeepGitDir);
        Assert.Equal("ADD src dst", instruction.ToString());

        // Set KeepGitDir = true via property
        instruction.KeepGitDir = true;
        Assert.True(instruction.KeepGitDir);
        Assert.Equal("ADD --keep-git-dir src dst", instruction.ToString());

        // Set KeepGitDir = false — flag should be removed
        instruction.KeepGitDir = false;
        Assert.False(instruction.KeepGitDir);
        Assert.Equal("ADD src dst", instruction.ToString());

        // Construct with keepGitDir = true directly in the constructor
        instruction = new(new string[] { "src" }, "dst", keepGitDir: true, escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.KeepGitDir);
        Assert.Equal("ADD --keep-git-dir src dst", instruction.ToString());

        // Toggle off again
        instruction.KeepGitDir = false;
        Assert.False(instruction.KeepGitDir);
        Assert.Equal("ADD src dst", instruction.ToString());

        // Parse from text with line-continuation escape and then set KeepGitDir
        instruction = AddInstruction.Parse($"ADD`\n src dst", '`');
        instruction.KeepGitDir = true;
        Assert.True(instruction.KeepGitDir);
        Assert.Equal($"ADD --keep-git-dir`\n src dst", instruction.ToString());

        // Parse with --keep-git-dir already present and remove it
        instruction = AddInstruction.Parse($"ADD`\n --keep-git-dir`\n src dst", '`');
        instruction.KeepGitDir = false;
        Assert.False(instruction.KeepGitDir);
        Assert.Equal($"ADD`\n`\n src dst", instruction.ToString());
    }

    [Fact]
    public void Link()
    {
        // Not set by default
        AddInstruction instruction = new(new string[] { "src" }, "dst", escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.False(instruction.Link);
        Assert.Equal("ADD src dst", instruction.ToString());

        // Set Link = true via property
        instruction.Link = true;
        Assert.True(instruction.Link);
        Assert.Equal("ADD --link src dst", instruction.ToString());

        // Set Link = false — flag should be removed
        instruction.Link = false;
        Assert.False(instruction.Link);
        Assert.Equal("ADD src dst", instruction.ToString());

        // Construct with link = true directly in the constructor
        instruction = new(new string[] { "src" }, "dst", link: true, escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.Link);
        Assert.Equal("ADD --link src dst", instruction.ToString());

        // Toggle off again
        instruction.Link = false;
        Assert.False(instruction.Link);
        Assert.Equal("ADD src dst", instruction.ToString());

        // Parse from text with line-continuation escape and then set Link
        instruction = AddInstruction.Parse($"ADD`\n src dst", '`');
        instruction.Link = true;
        Assert.True(instruction.Link);
        Assert.Equal($"ADD --link`\n src dst", instruction.ToString());

        // Parse with --link already present and remove it
        instruction = AddInstruction.Parse($"ADD`\n --link`\n src dst", '`');
        instruction.Link = false;
        Assert.False(instruction.Link);
        Assert.Equal($"ADD`\n`\n src dst", instruction.ToString());
    }

    [Fact]
    public void KeepGitDir_ExplicitTrue()
    {
        AddInstruction instruction = AddInstruction.Parse("ADD --keep-git-dir=true src dst");
        Assert.True(instruction.KeepGitDir);
        Assert.NotNull(instruction.KeepGitDirFlagToken);
        Assert.True(instruction.KeepGitDirFlagToken!.BoolValue);
        Assert.Equal("ADD --keep-git-dir=true src dst", instruction.ToString());
    }

    [Fact]
    public void KeepGitDir_ExplicitFalse()
    {
        AddInstruction instruction = AddInstruction.Parse("ADD --keep-git-dir=false src dst");
        Assert.False(instruction.KeepGitDir);
        Assert.NotNull(instruction.KeepGitDirFlagToken);
        Assert.False(instruction.KeepGitDirFlagToken!.BoolValue);
        Assert.Equal("ADD --keep-git-dir=false src dst", instruction.ToString());

        // Setting KeepGitDir = true should replace the =false flag with a bare flag
        instruction.KeepGitDir = true;
        Assert.True(instruction.KeepGitDir);
        Assert.Equal("ADD --keep-git-dir src dst", instruction.ToString());
    }

    [Fact]
    public void Link_ExplicitTrue()
    {
        AddInstruction instruction = AddInstruction.Parse("ADD --link=true src dst");
        Assert.True(instruction.Link);
        Assert.NotNull(instruction.LinkFlagToken);
        Assert.True(instruction.LinkFlagToken!.BoolValue);
        Assert.Equal("ADD --link=true src dst", instruction.ToString());
    }

    [Fact]
    public void Link_ExplicitFalse()
    {
        AddInstruction instruction = AddInstruction.Parse("ADD --link=false src dst");
        Assert.False(instruction.Link);
        Assert.NotNull(instruction.LinkFlagToken);
        Assert.False(instruction.LinkFlagToken!.BoolValue);
        Assert.Equal("ADD --link=false src dst", instruction.ToString());

        // Setting Link = true should replace the =false flag with a bare flag
        instruction.Link = true;
        Assert.True(instruction.Link);
        Assert.Equal("ADD --link src dst", instruction.ToString());
    }

    [Theory]
    [InlineData("ADD --link=True src dst", true)]
    [InlineData("ADD --link=FALSE src dst", false)]
    [InlineData("ADD --keep-git-dir=True src dst", true)]
    [InlineData("ADD --keep-git-dir=FALSE src dst", false)]
    public void BooleanFlag_CaseInsensitive(string text, bool expectedValue)
    {
        AddInstruction instruction = AddInstruction.Parse(text);
        // Check the appropriate flag based on what's in the text
        if (text.Contains("--link"))
        {
            Assert.Equal(expectedValue, instruction.Link);
        }
        else
        {
            Assert.Equal(expectedValue, instruction.KeepGitDir);
        }
        Assert.Equal(text, instruction.ToString());
    }

    [Fact]
    public void Checksum_WithChown()
    {
        AddInstruction instruction = new(
            new string[] { "src" }, "dst",
            changeOwner: "user",
            checksum: "sha256:abc123",
            escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.Equal("sha256:abc123", instruction.Checksum);
        Assert.Equal("user", instruction.ChangeOwner);
        Assert.Equal("ADD --checksum=sha256:abc123 --chown=user src dst", instruction.ToString());
    }

    [Fact]
    public void KeepGitDir_WithChown()
    {
        AddInstruction instruction = new(
            new string[] { "src" }, "dst",
            changeOwner: "user",
            keepGitDir: true,
            escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.KeepGitDir);
        Assert.Equal("user", instruction.ChangeOwner);
        Assert.Equal("ADD --chown=user --keep-git-dir src dst", instruction.ToString());
    }

    [Fact]
    public void Link_WithChown()
    {
        AddInstruction instruction = new(
            new string[] { "src" }, "dst",
            changeOwner: "user",
            link: true,
            escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.Link);
        Assert.Equal("user", instruction.ChangeOwner);
        Assert.Equal("ADD --chown=user --link src dst", instruction.ToString());
    }

    [Fact]
    public void Link_WithChmod()
    {
        AddInstruction instruction = new(
            new string[] { "src" }, "dst",
            permissions: "755",
            link: true,
            escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.Link);
        Assert.Equal("755", instruction.Permissions);
        Assert.Equal("ADD --chmod=755 --link src dst", instruction.ToString());
    }

    [Fact]
    public void UnpackProperty()
    {
        // Not set by default
        AddInstruction instruction = new(new string[] { "src" }, "dst", escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.False(instruction.Unpack);
        Assert.Equal("ADD src dst", instruction.ToString());

        // Set Unpack = true via property
        instruction.Unpack = true;
        Assert.True(instruction.Unpack);
        Assert.Equal("ADD --unpack src dst", instruction.ToString());

        // Set Unpack = false -- flag should be removed
        instruction.Unpack = false;
        Assert.False(instruction.Unpack);
        Assert.Equal("ADD src dst", instruction.ToString());

        // Construct with unpack = true directly in the constructor
        instruction = new(new string[] { "src" }, "dst", unpack: true, escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.Unpack);
        Assert.Equal("ADD --unpack src dst", instruction.ToString());

        // Toggle off again
        instruction.Unpack = false;
        Assert.False(instruction.Unpack);
        Assert.Equal("ADD src dst", instruction.ToString());

        // Parse from text with line-continuation escape and then set Unpack
        instruction = AddInstruction.Parse($"ADD`\n src dst", '`');
        instruction.Unpack = true;
        Assert.True(instruction.Unpack);
        Assert.Equal($"ADD --unpack`\n src dst", instruction.ToString());

        // Parse with --unpack already present and remove it
        instruction = AddInstruction.Parse($"ADD`\n --unpack`\n src dst", '`');
        instruction.Unpack = false;
        Assert.False(instruction.Unpack);
        Assert.Equal($"ADD`\n`\n src dst", instruction.ToString());
    }

    [Fact]
    public void Unpack_WithChown()
    {
        AddInstruction instruction = new(
            new string[] { "src" }, "dst",
            changeOwner: "user",
            unpack: true,
            escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.Unpack);
        Assert.Equal("user", instruction.ChangeOwner);
        Assert.Equal("ADD --chown=user --unpack src dst", instruction.ToString());
    }

    [Fact]
    public void ExcludesProperty()
    {
        // Not set by default
        AddInstruction instruction = new(new string[] { "src" }, "dst", escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.Empty(instruction.Excludes);
        Assert.Equal("ADD src dst", instruction.ToString());

        // Parse single --exclude
        instruction = AddInstruction.Parse("ADD --exclude=*.txt src dst");
        Assert.Single(instruction.Excludes);
        Assert.Equal("*.txt", instruction.Excludes[0]);
        Assert.Equal("ADD --exclude=*.txt src dst", instruction.ToString());

        // Parse multiple --exclude flags
        instruction = AddInstruction.Parse("ADD --exclude=*.txt --exclude=docs/ src dst");
        Assert.Equal(2, instruction.Excludes.Count);
        Assert.Equal("*.txt", instruction.Excludes[0]);
        Assert.Equal("docs/", instruction.Excludes[1]);
        Assert.Equal("ADD --exclude=*.txt --exclude=docs/ src dst", instruction.ToString());

        // Construct with excludes directly in the constructor
        instruction = new(new string[] { "src" }, "dst",
            excludes: new string[] { "*.log", "temp/" },
            escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.Equal(2, instruction.Excludes.Count);
        Assert.Equal("*.log", instruction.Excludes[0]);
        Assert.Equal("temp/", instruction.Excludes[1]);
        Assert.Equal("ADD --exclude=*.log --exclude=temp/ src dst", instruction.ToString());
    }

    [Fact]
    public void Excludes_WithChown()
    {
        AddInstruction instruction = new(
            new string[] { "src" }, "dst",
            changeOwner: "user",
            excludes: new string[] { "*.txt" },
            escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.Single(instruction.Excludes);
        Assert.Equal("*.txt", instruction.Excludes[0]);
        Assert.Equal("user", instruction.ChangeOwner);
        Assert.Equal("ADD --chown=user --exclude=*.txt src dst", instruction.ToString());
    }

    [Fact]
    public void Excludes_WithLineContinuation()
    {
        // Parse with --exclude and line continuation
        AddInstruction instruction = AddInstruction.Parse($"ADD --exclude=*.txt `\n src dst", '`');
        Assert.Single(instruction.Excludes);
        Assert.Equal("*.txt", instruction.Excludes[0]);
        Assert.Equal($"ADD --exclude=*.txt `\n src dst", instruction.ToString());
    }

    [Fact]
    public void AllNewFlags_Together()
    {
        // Combine --unpack with --exclude flags
        AddInstruction instruction = AddInstruction.Parse("ADD --unpack --exclude=*.txt --exclude=docs/ src dst");
        Assert.True(instruction.Unpack);
        Assert.Equal(2, instruction.Excludes.Count);
        Assert.Equal("*.txt", instruction.Excludes[0]);
        Assert.Equal("docs/", instruction.Excludes[1]);
        Assert.Equal("ADD --unpack --exclude=*.txt --exclude=docs/ src dst", instruction.ToString());

        // Construct with all new flags in the constructor
        instruction = new(new string[] { "src" }, "dst",
            unpack: true,
            excludes: new string[] { "*.txt", "docs/" },
            escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.Unpack);
        Assert.Equal(2, instruction.Excludes.Count);
        Assert.Equal("ADD --unpack --exclude=*.txt --exclude=docs/ src dst", instruction.ToString());
    }

    [Fact]
    public void AllFlags_Together()
    {
        // Combine all flags together
        AddInstruction instruction = AddInstruction.Parse(
            "ADD --checksum=sha256:abc123 --keep-git-dir --link --unpack --exclude=*.txt --chown=user --chmod=755 src dst");
        Assert.Equal("sha256:abc123", instruction.Checksum);
        Assert.True(instruction.KeepGitDir);
        Assert.True(instruction.Link);
        Assert.True(instruction.Unpack);
        Assert.Single(instruction.Excludes);
        Assert.Equal("*.txt", instruction.Excludes[0]);
        Assert.Equal("user", instruction.ChangeOwner);
        Assert.Equal("755", instruction.Permissions);
    }

    public static IEnumerable<object[]> ParseTestInputBase() => ParseTestInput("ADD");

    public static IEnumerable<object[]> CreateTestInputBase() => CreateTestInput("ADD");

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<AddInstruction>[] testInputs = new ParseTestScenario<AddInstruction>[]
        {
            // --checksum alone
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --checksum=sha256:abc123 src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ChecksumFlag>(token, "checksum", "sha256:abc123"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ADD", result.InstructionName);
                    Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                    Assert.Equal("dst", result.Destination);
                    Assert.Equal("sha256:abc123", result.Checksum);
                    Assert.False(result.KeepGitDir);
                    Assert.False(result.Link);
                }
            },
            // --keep-git-dir alone
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --keep-git-dir src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeepGitDirFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ADD", result.InstructionName);
                    Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                    Assert.Equal("dst", result.Destination);
                    Assert.True(result.KeepGitDir);
                    Assert.Null(result.Checksum);
                    Assert.False(result.Link);
                }
            },
            // --link alone
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --link src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ADD", result.InstructionName);
                    Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                    Assert.Equal("dst", result.Destination);
                    Assert.True(result.Link);
                    Assert.Null(result.Checksum);
                    Assert.False(result.KeepGitDir);
                }
            },
            // --checksum and --keep-git-dir together
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --checksum=sha256:abc123 --keep-git-dir src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ChecksumFlag>(token, "checksum", "sha256:abc123"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeepGitDirFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Equal("sha256:abc123", result.Checksum);
                    Assert.True(result.KeepGitDir);
                    Assert.False(result.Link);
                }
            },
            // --checksum and --link together
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --checksum=sha256:abc123 --link src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ChecksumFlag>(token, "checksum", "sha256:abc123"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Equal("sha256:abc123", result.Checksum);
                    Assert.False(result.KeepGitDir);
                    Assert.True(result.Link);
                }
            },
            // --keep-git-dir and --link together
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --keep-git-dir --link src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeepGitDirFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Null(result.Checksum);
                    Assert.True(result.KeepGitDir);
                    Assert.True(result.Link);
                }
            },
            // all three new flags together
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --checksum=sha256:abc123 --keep-git-dir --link src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ChecksumFlag>(token, "checksum", "sha256:abc123"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeepGitDirFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Equal("sha256:abc123", result.Checksum);
                    Assert.True(result.KeepGitDir);
                    Assert.True(result.Link);
                }
            },
            // --checksum with --chown
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --checksum=sha256:abc123 --chown=user src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ChecksumFlag>(token, "checksum", "sha256:abc123"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ChangeOwnerFlag>(token, "--chown=user",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "chown"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "user")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Equal("sha256:abc123", result.Checksum);
                    Assert.Equal("user", result.ChangeOwner);
                    Assert.False(result.KeepGitDir);
                    Assert.False(result.Link);
                }
            },
            // --link with --chmod
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --link --chmod=755 src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ChangeModeFlag>(token, "--chmod=755",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "chmod"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "755")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.True(result.Link);
                    Assert.Equal("755", result.Permissions);
                    Assert.Null(result.Checksum);
                    Assert.False(result.KeepGitDir);
                }
            },
            // any-order: --link before --checksum
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --link --checksum=sha256:abc123 src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ChecksumFlag>(token, "checksum", "sha256:abc123"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Equal("sha256:abc123", result.Checksum);
                    Assert.True(result.Link);
                    Assert.False(result.KeepGitDir);
                }
            },
            // any-order: --link before --keep-git-dir
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --link --keep-git-dir src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeepGitDirFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Null(result.Checksum);
                    Assert.True(result.Link);
                    Assert.True(result.KeepGitDir);
                }
            },
            // variable checksum
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --checksum=$CHECKSUM src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ChecksumFlag>(token, "--checksum=$CHECKSUM",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "checksum"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<LiteralToken>(token, "$CHECKSUM",
                            token => ValidateAggregate<VariableRefToken>(token, "$CHECKSUM",
                                token => ValidateString(token, "CHECKSUM")))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Equal("$CHECKSUM", result.Checksum);
                    Assert.False(result.KeepGitDir);
                    Assert.False(result.Link);
                }
            },
            // round-trip: --checksum with line continuation
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --checksum=sha256:abc123 `\n src dst",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ChecksumFlag>(token, "checksum", "sha256:abc123"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Equal("sha256:abc123", result.Checksum);
                    Assert.Equal("ADD --checksum=sha256:abc123 `\n src dst", result.ToString());
                }
            },
            // round-trip: --keep-git-dir with line continuation
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --keep-git-dir `\n src dst",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeepGitDirFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.True(result.KeepGitDir);
                    Assert.Equal("ADD --keep-git-dir `\n src dst", result.ToString());
                }
            },
            // round-trip: --link with line continuation
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --link `\n src dst",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.True(result.Link);
                    Assert.Equal("ADD --link `\n src dst", result.ToString());
                }
            },
            // all three flags with --chown and --chmod
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --checksum=sha256:abc123 --keep-git-dir --chown=user --chmod=755 --link src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ChecksumFlag>(token, "checksum", "sha256:abc123"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeepGitDirFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ChangeOwnerFlag>(token, "--chown=user",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "chown"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "user")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ChangeModeFlag>(token, "--chmod=755",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "chmod"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "755")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Equal("sha256:abc123", result.Checksum);
                    Assert.True(result.KeepGitDir);
                    Assert.True(result.Link);
                    Assert.Equal("user", result.ChangeOwner);
                    Assert.Equal("755", result.Permissions);
                }
            },
            // --unpack alone
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --unpack src.tar /app",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateUnpackFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src.tar"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ADD", result.InstructionName);
                    Assert.Equal(new string[] { "src.tar" }, result.Sources.ToArray());
                    Assert.Equal("/app", result.Destination);
                    Assert.True(result.Unpack);
                    Assert.Empty(result.Excludes);
                    Assert.Null(result.Checksum);
                    Assert.False(result.KeepGitDir);
                    Assert.False(result.Link);
                }
            },
            // --exclude alone (single)
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --exclude=*.txt src /app",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ExcludeFlag>(token, "exclude", "*.txt"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ADD", result.InstructionName);
                    Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                    Assert.Equal("/app", result.Destination);
                    Assert.Single(result.Excludes);
                    Assert.Equal("*.txt", result.Excludes[0]);
                    Assert.False(result.Unpack);
                    Assert.Null(result.Checksum);
                    Assert.False(result.KeepGitDir);
                    Assert.False(result.Link);
                }
            },
            // --exclude repeated multiple times
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --exclude=*.txt --exclude=docs/ src /app",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ExcludeFlag>(token, "exclude", "*.txt"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ExcludeFlag>(token, "exclude", "docs/"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app")
                },
                Validate = result =>
                {
                    Assert.Equal(2, result.Excludes.Count);
                    Assert.Equal("*.txt", result.Excludes[0]);
                    Assert.Equal("docs/", result.Excludes[1]);
                    Assert.False(result.Unpack);
                }
            },
            // --unpack with --exclude
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --unpack --exclude=*.txt src.tar /app",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateUnpackFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ExcludeFlag>(token, "exclude", "*.txt"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src.tar"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app")
                },
                Validate = result =>
                {
                    Assert.True(result.Unpack);
                    Assert.Single(result.Excludes);
                    Assert.Equal("*.txt", result.Excludes[0]);
                }
            },
            // --exclude with variable reference
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --exclude=$PATTERN src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExcludeFlag>(token, "--exclude=$PATTERN",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "exclude"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<LiteralToken>(token, "$PATTERN",
                            token => ValidateAggregate<VariableRefToken>(token, "$PATTERN",
                                token => ValidateString(token, "PATTERN")))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Single(result.Excludes);
                    Assert.Equal("$PATTERN", result.Excludes[0]);
                }
            },
            // round-trip: --unpack with line continuation
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --unpack `\n src.tar /app",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateUnpackFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src.tar"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app")
                },
                Validate = result =>
                {
                    Assert.True(result.Unpack);
                    Assert.Equal("ADD --unpack `\n src.tar /app", result.ToString());
                }
            },
            // round-trip: --exclude with line continuation
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --exclude=*.txt `\n src dst",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ExcludeFlag>(token, "exclude", "*.txt"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Single(result.Excludes);
                    Assert.Equal("*.txt", result.Excludes[0]);
                    Assert.Equal("ADD --exclude=*.txt `\n src dst", result.ToString());
                }
            },
            // all flags together including --unpack and --exclude
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD --checksum=sha256:abc123 --keep-git-dir --link --unpack --exclude=*.txt --chown=user --chmod=755 src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ChecksumFlag>(token, "checksum", "sha256:abc123"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeepGitDirFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateUnpackFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ExcludeFlag>(token, "exclude", "*.txt"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ChangeOwnerFlag>(token, "--chown=user",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "chown"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "user")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ChangeModeFlag>(token, "--chmod=755",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "chmod"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "755")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Equal("sha256:abc123", result.Checksum);
                    Assert.True(result.KeepGitDir);
                    Assert.True(result.Link);
                    Assert.True(result.Unpack);
                    Assert.Single(result.Excludes);
                    Assert.Equal("*.txt", result.Excludes[0]);
                    Assert.Equal("user", result.ChangeOwner);
                    Assert.Equal("755", result.Permissions);
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public static IEnumerable<object[]> CreateTestInput()
    {
        AddInstructionCreateTestScenario[] testInputs = new AddInstructionCreateTestScenario[]
        {
            // Create with checksum only
            new AddInstructionCreateTestScenario
            {
                Sources = new string[] { "src" },
                Destination = "dst",
                Checksum = "sha256:abc123",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ChecksumFlag>(token, "checksum", "sha256:abc123"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Equal("sha256:abc123", result.Checksum);
                    Assert.False(result.KeepGitDir);
                    Assert.False(result.Link);
                    Assert.Equal("ADD --checksum=sha256:abc123 src dst", result.ToString());
                }
            },
            // Create with keepGitDir only
            new AddInstructionCreateTestScenario
            {
                Sources = new string[] { "src" },
                Destination = "dst",
                KeepGitDir = true,
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeepGitDirFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Null(result.Checksum);
                    Assert.True(result.KeepGitDir);
                    Assert.False(result.Link);
                    Assert.Equal("ADD --keep-git-dir src dst", result.ToString());
                }
            },
            // Create with link only
            new AddInstructionCreateTestScenario
            {
                Sources = new string[] { "src" },
                Destination = "dst",
                Link = true,
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Null(result.Checksum);
                    Assert.False(result.KeepGitDir);
                    Assert.True(result.Link);
                    Assert.Equal("ADD --link src dst", result.ToString());
                }
            },
            // Create with all three new flags
            new AddInstructionCreateTestScenario
            {
                Sources = new string[] { "src" },
                Destination = "dst",
                Checksum = "sha256:abc123",
                KeepGitDir = true,
                Link = true,
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ChecksumFlag>(token, "checksum", "sha256:abc123"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeepGitDirFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Equal("sha256:abc123", result.Checksum);
                    Assert.True(result.KeepGitDir);
                    Assert.True(result.Link);
                    Assert.Equal("ADD --checksum=sha256:abc123 --keep-git-dir --link src dst", result.ToString());
                }
            },
            // Create with unpack only
            new AddInstructionCreateTestScenario
            {
                Sources = new string[] { "src.tar" },
                Destination = "/app",
                Unpack = true,
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateUnpackFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src.tar"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app")
                },
                Validate = result =>
                {
                    Assert.True(result.Unpack);
                    Assert.Empty(result.Excludes);
                    Assert.Equal("ADD --unpack src.tar /app", result.ToString());
                }
            },
            // Create with single exclude
            new AddInstructionCreateTestScenario
            {
                Sources = new string[] { "src" },
                Destination = "dst",
                Excludes = new string[] { "*.txt" },
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ExcludeFlag>(token, "exclude", "*.txt"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Single(result.Excludes);
                    Assert.Equal("*.txt", result.Excludes[0]);
                    Assert.False(result.Unpack);
                    Assert.Equal("ADD --exclude=*.txt src dst", result.ToString());
                }
            },
            // Create with multiple excludes
            new AddInstructionCreateTestScenario
            {
                Sources = new string[] { "src" },
                Destination = "dst",
                Excludes = new string[] { "*.txt", "docs/" },
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ExcludeFlag>(token, "exclude", "*.txt"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ExcludeFlag>(token, "exclude", "docs/"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Equal(2, result.Excludes.Count);
                    Assert.Equal("*.txt", result.Excludes[0]);
                    Assert.Equal("docs/", result.Excludes[1]);
                    Assert.Equal("ADD --exclude=*.txt --exclude=docs/ src dst", result.ToString());
                }
            },
            // Create with unpack and excludes together
            new AddInstructionCreateTestScenario
            {
                Sources = new string[] { "src.tar" },
                Destination = "/app",
                Unpack = true,
                Excludes = new string[] { "*.txt" },
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateUnpackFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<ExcludeFlag>(token, "exclude", "*.txt"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src.tar"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app")
                },
                Validate = result =>
                {
                    Assert.True(result.Unpack);
                    Assert.Single(result.Excludes);
                    Assert.Equal("*.txt", result.Excludes[0]);
                    Assert.Equal("ADD --unpack --exclude=*.txt src.tar /app", result.ToString());
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    private static void ValidateKeepGitDirFlag(Token token)
    {
        ValidateAggregate<KeepGitDirFlag>(token, "--keep-git-dir",
            token => ValidateSymbol(token, '-'),
            token => ValidateSymbol(token, '-'),
            token => ValidateKeyword(token, "keep-git-dir"));
    }

    private static void ValidateLinkFlag(Token token)
    {
        ValidateAggregate<LinkFlag>(token, "--link",
            token => ValidateSymbol(token, '-'),
            token => ValidateSymbol(token, '-'),
            token => ValidateKeyword(token, "link"));
    }

    private static void ValidateUnpackFlag(Token token)
    {
        ValidateAggregate<UnpackFlag>(token, "--unpack",
            token => ValidateSymbol(token, '-'),
            token => ValidateSymbol(token, '-'),
            token => ValidateKeyword(token, "unpack"));
    }

    public class AddInstructionCreateTestScenario : TestScenario<AddInstruction>
    {
        public IEnumerable<string> Sources { get; set; }
        public string Destination { get; set; }
        public string ChangeOwner { get; set; }
        public string Permissions { get; set; }
        public string Checksum { get; set; }
        public bool KeepGitDir { get; set; }
        public bool Link { get; set; }
        public bool Unpack { get; set; }
        public IEnumerable<string> Excludes { get; set; }
        public char EscapeChar { get; set; } = Dockerfile.DefaultEscapeChar;
    }
}
