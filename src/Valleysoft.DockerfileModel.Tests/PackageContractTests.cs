using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace Valleysoft.DockerfileModel.Tests;

public class PackageContractTests
{
    private const string PackageId = "Valleysoft.DockerfileModel";
    private static readonly string[] Frameworks = ["netstandard2.0", "net10.0"];

    [PackageFact]
    public void PublicationArchivesMeetContract()
    {
        string directory = Environment.GetEnvironmentVariable("PACKAGE_DIRECTORY")!;
        string version = Environment.GetEnvironmentVariable("PACKAGE_VERSION")!;
        string commit = Environment.GetEnvironmentVariable("PACKAGE_COMMIT")!;
        Assert.False(string.IsNullOrWhiteSpace(version), "PACKAGE_VERSION must specify the expected version.");
        Assert.Matches("^[0-9a-f]{40}$", commit);
        Assert.True(Directory.Exists(directory), $"Package directory does not exist: {directory}");
        string package = Assert.Single(Directory.GetFiles(directory, "*.nupkg"));
        string symbols = Assert.Single(Directory.GetFiles(directory, "*.snupkg"));
        Assert.Equal($"{PackageId}.{version}.nupkg", Path.GetFileName(package));
        Assert.Equal($"{PackageId}.{version}.snupkg", Path.GetFileName(symbols));
        using ZipArchive archive = ZipFile.OpenRead(package);
        using ZipArchive symbolArchive = ZipFile.OpenRead(symbols);
        ValidateArchive(archive, version, commit, symbols: false);
        ValidateArchive(symbolArchive, version, commit, symbols: true);
        foreach (string framework in Frameworks)
        {
            using Stream dll = OpenEntry(archive, $"lib/{framework}/{PackageId}.dll");
            using Stream pdb = OpenEntry(symbolArchive, $"lib/{framework}/{PackageId}.pdb");
            ValidateSymbols(dll, pdb, commit);
        }
    }

    [Theory]
    [InlineData("2.0.2", "2.0.1", false)]
    [InlineData("2.0.1-preview.1", "2.0.1", false)]
    [InlineData("2.0.1", "2.0.1", true)]
    [InlineData("2.0.1-preview.1", "2.0.1-preview.1", true)]
    public void ChecksVersionInsideArchive(string actual, string expected, bool succeeds)
    {
        XElement metadata = new("metadata", new XElement("id", PackageId), new XElement("version", actual));
        Exception? error = Record.Exception(() => ValidateIdentity(metadata, expected));
        Assert.Equal(succeeds, error is null);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("lib/net10.0/Valleysoft.DockerfileModel.xml")]
    [InlineData("lib/netstandard2.0/Valleysoft.DockerfileModel.dll")]
    public void RejectsMissingContractFiles(string missing)
    {
        string[] expected = ExpectedFiles(symbols: false);
        Assert.NotNull(Record.Exception(() => ValidateFileList(expected.Where(name => name != missing), symbols: false)));
    }

    [Theory]
    [InlineData("lib/net8.0/Valleysoft.DockerfileModel.dll")]
    [InlineData("lib/net10.0/Valleysoft.DockerfileModel.TestSupport.dll")]
    [InlineData("tools/install.ps1")]
    public void RejectsUnintendedFiles(string extra)
    {
        Assert.NotNull(Record.Exception(() => ValidateFileList(ExpectedFiles(symbols: false).Append(extra), symbols: false)));
    }

    private static void ValidateArchive(ZipArchive archive, string version, string commit, bool symbols)
    {
        ValidateFileList(archive.Entries.Select(entry => entry.FullName), symbols);
        Assert.All(archive.Entries, entry =>
        {
            Assert.True(entry.Length > 0, $"Empty package entry: {entry.FullName}");
        });
        using Stream nuspec = OpenEntry(archive, $"{PackageId}.nuspec");
        XElement root = XDocument.Load(nuspec).Root!;
        XNamespace ns = root.Name.Namespace;
        XElement metadata = root.Element(ns + "metadata")!;
        ValidateIdentity(metadata, version);
        XElement repository = metadata.Element(ns + "repository")!;
        Assert.NotNull(repository);
        Assert.Equal("git", (string?)repository.Attribute("type"));
        Assert.Equal("https://github.com/mthalman/DockerfileModel", (string?)repository.Attribute("url"));
        Assert.Equal(commit, (string?)repository.Attribute("commit"));
        if (symbols)
        {
            Assert.Equal("SymbolsPackage", (string?)metadata.Element(ns + "packageTypes")?.Element(ns + "packageType")?.Attribute("name"));
            return;
        }

        Assert.Equal("MIT", (string?)metadata.Element(ns + "license"));
        Assert.Equal("expression", (string?)metadata.Element(ns + "license")?.Attribute("type"));
        Assert.Equal("README.md", (string?)metadata.Element(ns + "readme"));
        using Stream readme = OpenEntry(archive, "README.md");
        using StreamReader reader = new(readme);
        Assert.Contains("# Dockerfile Model Library", reader.ReadToEnd());
        XElement dependencies = metadata.Element(ns + "dependencies")!;
        XElement[] groups = dependencies.Elements(ns + "group").ToArray();
        Assert.Equal(2, groups.Length);
        foreach (string framework in Frameworks)
        {
            string target = framework == "netstandard2.0" ? ".NETStandard2.0" : framework;
            XElement group = Assert.Single(groups, item => (string?)item.Attribute("targetFramework") == target);
            Dictionary<string, string> expected = new()
            {
                ["Sprache"] = "2.3.1",
                ["Validation"] = "2.6.68"
            };
            if (framework == "netstandard2.0")
            {
                expected["System.Text.RegularExpressions"] = "4.3.1";
            }
            Dictionary<string, string> actual = group.Elements(ns + "dependency")
                .ToDictionary(item => (string)item.Attribute("id")!, item => (string)item.Attribute("version")!);
            Assert.Equal(expected.OrderBy(item => item.Key), actual.OrderBy(item => item.Key));
            using Stream docs = OpenEntry(archive, $"lib/{framework}/{PackageId}.xml");
            XElement doc = XDocument.Load(docs).Root!;
            Assert.Equal(PackageId, (string?)doc.Element("assembly")?.Element("name"));
            Assert.NotEmpty(doc.Element("members")!.Elements("member"));
        }
    }

    private static void ValidateIdentity(XElement metadata, string version)
    {
        XNamespace ns = metadata.Name.Namespace;
        Assert.Equal(PackageId, (string?)metadata.Element(ns + "id"));
        Assert.Equal(version, (string?)metadata.Element(ns + "version"));
    }

    private static void ValidateFileList(IEnumerable<string> names, bool symbols) =>
        Assert.Equal(ExpectedFiles(symbols).Order(), names.Order());

    private static string[] ExpectedFiles(bool symbols)
    {
        List<string> names =
        [
            "_rels/.rels", $"{PackageId}.nuspec", "[Content_Types].xml",
            "package/services/metadata/core-properties/nuget.psmdcp"
        ];
        if (!symbols)
        {
            names.Add("README.md");
        }
        foreach (string framework in Frameworks)
        {
            names.Add($"lib/{framework}/{PackageId}.{(symbols ? "pdb" : "dll")}");
            if (!symbols)
            {
                names.Add($"lib/{framework}/{PackageId}.xml");
            }
        }
        return names.ToArray();
    }

    private static Stream OpenEntry(ZipArchive archive, string name)
    {
        ZipArchiveEntry? entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        MemoryStream stream = new();
        using Stream source = entry.Open();
        source.CopyTo(stream);
        stream.Position = 0;
        return stream;
    }

    private static void ValidateSymbols(Stream dll, Stream pdb, string commit)
    {
        using PEReader pe = new(dll);
        DebugDirectoryEntry codeViewEntry = Assert.Single(pe.ReadDebugDirectory(), entry => entry.Type == DebugDirectoryEntryType.CodeView);
        CodeViewDebugDirectoryData codeView = pe.ReadCodeViewDebugDirectoryData(codeViewEntry);
        using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(pdb, MetadataStreamOptions.LeaveOpen);
        MetadataReader metadata = provider.GetMetadataReader();
        Assert.NotNull(metadata.DebugMetadataHeader);
        BlobContentId id = new(metadata.DebugMetadataHeader.Id);
        Assert.Equal(codeView.Guid, id.Guid);
        Assert.Equal(codeViewEntry.Stamp, id.Stamp);
        DebugDirectoryEntry checksumEntry = Assert.Single(pe.ReadDebugDirectory(), entry => entry.Type == DebugDirectoryEntryType.PdbChecksum);
        PdbChecksumDebugDirectoryData checksum = pe.ReadPdbChecksumDebugDirectoryData(checksumEntry);
        Assert.Equal("SHA256", checksum.AlgorithmName);
        // Portable PDB checksums zero the content ID when computing the hash.
        pdb.Position = 0;
        using MemoryStream content = new();
        pdb.CopyTo(content);
        byte[] bytes = content.ToArray();
        Array.Clear(bytes, metadata.DebugMetadataHeader.IdStartOffset, 20);
        Assert.Equal(checksum.Checksum.ToArray(), SHA256.HashData(bytes));

        Guid sourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
        CustomDebugInformation sourceLink = Assert.Single(metadata.CustomDebugInformation
            .Select(metadata.GetCustomDebugInformation), item => metadata.GetGuid(item.Kind) == sourceLinkKind);
        using JsonDocument json = JsonDocument.Parse(metadata.GetBlobBytes(sourceLink.Value));
        JsonProperty[] mappings = json.RootElement.GetProperty("documents").EnumerateObject().ToArray();
        Assert.NotEmpty(mappings);
        foreach (JsonProperty mapping in mappings)
        {
            Assert.EndsWith("*", mapping.Name);
            Assert.Equal($"https://raw.githubusercontent.com/mthalman/DockerfileModel/{commit}/*", mapping.Value.GetString());
        }
        Assert.NotEmpty(metadata.Documents);
        foreach (DocumentHandle handle in metadata.Documents)
        {
            string name = metadata.GetString(metadata.GetDocument(handle).Name);
            Assert.Contains(mappings, mapping => name.StartsWith(mapping.Name[..^1], StringComparison.Ordinal));
        }
    }
}

public sealed class PackageFactAttribute : FactAttribute
{
    public PackageFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PACKAGE_DIRECTORY") is null)
        {
            Skip = "Run Validate-Package.ps1 after packing to validate publication artifacts.";
        }
    }
}
