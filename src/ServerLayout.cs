using System.Xml.Linq;

namespace Bento;

public sealed record ServerLayout(string RepoPath, string Prefix)
{
    // Newest first, so a checkout that somehow carries both resolves to the current generation.
    private static readonly string[] KnownPrefixes = ["SPTushonka", "SPTarkov"];

    /// <summary>
    /// Detects the project prefix of the server checkout at repoPath, throwing when neither generation is present.
    /// </summary>
    public static ServerLayout Detect(string repoPath)
    {
        foreach (var prefix in KnownPrefixes)
        {
            if (Directory.Exists(Path.Combine(repoPath, "Libraries", $"{prefix}.Server.Core")))
            {
                return new ServerLayout(repoPath, prefix);
            }
        }

        throw new BentoException(
            $"No recognised server library layout under {repoPath}.",
            $"Expected Libraries/{KnownPrefixes[0]}.Server.Core. Is the server-csharp checkout complete?"
        );
    }

    /// <summary>
    /// The server executable project, relative to the repo root.
    /// </summary>
    public string ServerProjectFile => Path.Combine($"{Prefix}.Server", $"{Prefix}.Server.csproj");

    /// <summary>
    /// The publish output directory for one platform.
    /// </summary>
    public string PublishDir(string buildConfig, string platform)
    {
        return Path.Combine(RepoPath, $"{Prefix}.Server", "bin", buildConfig, "net10.0", platform, "publish");
    }

    /// <summary>
    /// A file inside the server's asset library, addressed by the path segments below SPT_Data.
    /// </summary>
    public string AssetPath(params string[] segments)
    {
        return Path.Combine([RepoPath, "Libraries", $"{Prefix}.Server.Assets", "SPT_Data", .. segments]);
    }

    /// <summary>
    /// The package ids dotnet pack emits for the server libraries, read from the checkout so a renamed or newly added
    /// library is picked up without a Bento release.
    /// </summary>
    public IReadOnlyList<string> PackageIds()
    {
        var libraries = Path.Combine(RepoPath, "Libraries");
        if (!Directory.Exists(libraries))
        {
            throw new BentoException($"No Libraries directory under {RepoPath}.");
        }

        var ids = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(libraries))
        {
            foreach (var csproj in Directory.EnumerateFiles(directory, "*.csproj"))
            {
                if (PackageIdOf(csproj) is { } id)
                {
                    ids.Add(id);
                }
            }
        }

        ids.Sort(StringComparer.Ordinal);
        return ids;
    }

    // Mirrors how dotnet pack resolves an id: PackageId, else AssemblyName, else the project filename. An explicit
    // IsPackable of false is the only thing that stops a package being emitted at all.
    private static string? PackageIdOf(string csproj)
    {
        var project = XDocument.Load(csproj);
        var packable = project.Descendants("IsPackable").LastOrDefault()?.Value.Trim();
        if (string.Equals(packable, "false", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return project.Descendants("PackageId").FirstOrDefault()?.Value.Trim()
            ?? project.Descendants("AssemblyName").FirstOrDefault()?.Value.Trim()
            ?? Path.GetFileNameWithoutExtension(csproj);
    }
}
