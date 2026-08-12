using NUnit.Framework;

namespace Bento.Tests;

/// <summary>
/// Tests for how ServerLayout resolves paths and package ids across the SPTarkov/SPTushonka rename.
/// </summary>
[TestFixture]
public class ServerLayoutTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"bento-tests-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// A renamed checkout resolves to the SPTushonka prefix and its project paths.
    /// </summary>
    [Test]
    public void DetectsRenamedLayout()
    {
        MakeLibrary("SPTushonka.Server.Core", packageId: "SPTushonka.Server.Core");

        var layout = ServerLayout.Detect(_root);

        Assert.Multiple(() =>
        {
            Assert.That(layout.Prefix, Is.EqualTo("SPTushonka"));
            Assert.That(
                layout.ServerProjectFile,
                Is.EqualTo(Path.Combine("SPTushonka.Server", "SPTushonka.Server.csproj"))
            );
            Assert.That(layout.AssetPath("configs", "core.json"), Does.Contain("SPTushonka.Server.Assets"));
        });
    }

    /// <summary>
    /// A pre-rename checkout still resolves, so older tags stay buildable.
    /// </summary>
    [Test]
    public void DetectsLegacyLayout()
    {
        MakeLibrary("SPTarkov.Server.Core", packageId: "SPTarkov.Server.Core");

        var layout = ServerLayout.Detect(_root);

        Assert.Multiple(() =>
        {
            Assert.That(layout.Prefix, Is.EqualTo("SPTarkov"));
            Assert.That(layout.PublishDir("Release", "win-x64"), Does.Contain("SPTarkov.Server"));
        });
    }

    /// <summary>
    /// A checkout with neither generation is an error rather than a silently wrong path.
    /// </summary>
    [Test]
    public void UnknownLayoutThrows()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Libraries", "Something.Else"));

        Assert.That(() => ServerLayout.Detect(_root), Throws.TypeOf<BentoException>());
    }

    /// <summary>
    /// Package ids come from PackageId, fall back to AssemblyName, and skip projects marked unpackable.
    /// </summary>
    [Test]
    public void ReadsPackageIdsFromProjects()
    {
        MakeLibrary("SPTushonka.Server.Core", packageId: "SPTushonka.Server.Core");
        MakeLibrary("SPTushonka.Common", assemblyName: "SPTarkov.Common");
        MakeLibrary("SPTushonka.Internal", packageId: "SPTushonka.Internal", packable: false);

        Assert.That(
            ServerLayout.Detect(_root).PackageIds(),
            Is.EqualTo(new[] { "SPTarkov.Common", "SPTushonka.Server.Core" })
        );
    }

    private void MakeLibrary(string name, string? packageId = null, string? assemblyName = null, bool packable = true)
    {
        var directory = Path.Combine(_root, "Libraries", name);
        Directory.CreateDirectory(directory);

        var properties = string.Concat(
            packageId is null ? "" : $"<PackageId>{packageId}</PackageId>",
            assemblyName is null ? "" : $"<AssemblyName>{assemblyName}</AssemblyName>",
            packable ? "" : "<IsPackable>false</IsPackable>"
        );
        File.WriteAllText(
            Path.Combine(directory, $"{name}.csproj"),
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>{properties}</PropertyGroup></Project>"
        );
    }
}
