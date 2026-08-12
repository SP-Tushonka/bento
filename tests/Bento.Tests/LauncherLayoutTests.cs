using NUnit.Framework;

namespace Bento.Tests;

/// <summary>
/// Tests for how LauncherLayout resolves the launcher project across the rename and flattening of project/.
/// </summary>
[TestFixture]
public class LauncherLayoutTests
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
    /// The flattened, renamed launcher builds from the repo root.
    /// </summary>
    [Test]
    public void DetectsFlattenedLayout()
    {
        MakeProject(_root, "SPTushonka");

        var layout = LauncherLayout.Detect(_root);

        Assert.Multiple(() =>
        {
            Assert.That(layout.ProjectRoot, Is.EqualTo(_root));
            Assert.That(layout.Prefix, Is.EqualTo("SPTushonka"));
            Assert.That(layout.BuildDir, Is.EqualTo(Path.Combine(_root, "Build")));
        });
    }

    /// <summary>
    /// A pre-rename checkout still builds out of project/, so older tags stay buildable.
    /// </summary>
    [Test]
    public void DetectsLegacyProjectLayout()
    {
        var project = Path.Combine(_root, "project");
        MakeProject(project, "SPTarkov");

        var layout = LauncherLayout.Detect(_root);

        Assert.Multiple(() =>
        {
            Assert.That(layout.ProjectRoot, Is.EqualTo(project));
            Assert.That(layout.Prefix, Is.EqualTo("SPTarkov"));
            Assert.That(
                layout.CsprojRelative,
                Is.EqualTo(Path.Combine("SPTarkov.Launcher", "SPTarkov.Launcher.csproj"))
            );
        });
    }

    /// <summary>
    /// A checkout with no known launcher project is an error rather than a failed publish later on.
    /// </summary>
    [Test]
    public void UnknownLayoutThrows()
    {
        Assert.That(() => LauncherLayout.Detect(_root), Throws.TypeOf<BentoException>());
    }

    private static void MakeProject(string root, string prefix)
    {
        var directory = Path.Combine(root, $"{prefix}.Launcher");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{prefix}.Launcher.csproj"), "<Project />");
    }
}
