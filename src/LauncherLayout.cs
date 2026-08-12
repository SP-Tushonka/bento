namespace Bento;

public sealed record LauncherLayout(string ProjectRoot, string Prefix)
{
    // Newest arrangement first. The launcher was flattened and renamed in the same change, but probing the
    // combinations keeps a half-applied rename buildable.
    private static readonly string[] KnownPrefixes = ["SPTushonka", "SPTarkov"];

    /// <summary>
    /// Detects the launcher arrangement at repoPath, throwing when no known project layout is present.
    /// </summary>
    public static LauncherLayout Detect(string repoPath)
    {
        foreach (var root in new[] { repoPath, Path.Combine(repoPath, "project") })
        {
            foreach (var prefix in KnownPrefixes)
            {
                if (File.Exists(Path.Combine(root, $"{prefix}.Launcher", $"{prefix}.Launcher.csproj")))
                {
                    return new LauncherLayout(root, prefix);
                }
            }
        }

        throw new BentoException(
            $"No recognised launcher project layout under {repoPath}.",
            $"Expected {KnownPrefixes[0]}.Launcher. Bento targets the launcher build system from 4.1 onward."
        );
    }

    /// <summary>
    /// The launcher project, relative to the project root that dotnet runs in.
    /// </summary>
    public string CsprojRelative => Path.Combine($"{Prefix}.Launcher", $"{Prefix}.Launcher.csproj");

    /// <summary>
    /// The Build directory the launcher's SPTBuildEvent target populates.
    /// </summary>
    public string BuildDir => Path.Combine(ProjectRoot, "Build");

    /// <summary>
    /// The published Windows executable for one platform, before SPTBuildEvent copies it into Build.
    /// </summary>
    public string PublishedExe(string platform, string fileName)
    {
        return Path.Combine(
            ProjectRoot,
            $"{Prefix}.Launcher",
            "bin",
            "Release",
            "net10.0",
            platform,
            "publish",
            fileName
        );
    }
}
