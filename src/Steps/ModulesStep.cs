namespace Bento.Steps;

/// <summary>
/// Builds the modules project against the module package and returns the Build directory. Mono builds (4.x) unpack
/// the package into Shared/Managed inside the repo. Il2cpp builds (5.x) unpack it into a folder shaped like a game
/// install and hand that to the build as GameDir.
/// </summary>
public static class ModulesStep
{
    public const string Stage = "modules";

    private static readonly string[] Il2CppPackageFolders =
    [
        Path.Combine("BepInEx", "interop"),
        Path.Combine("BepInEx", "core"),
    ];

    public static async Task<string> RunAsync(
        BuildContext ctx,
        ResolvedModulePackage modulePackage,
        BuildLogger log,
        CancellationToken cancellationToken = default
    )
    {
        var repoPath = ctx.Modules!.Path;
        return ctx.Il2Cpp
            ? await BuildIl2CppAsync(ctx, repoPath, modulePackage, log, cancellationToken)
            : await BuildMonoAsync(ctx, repoPath, modulePackage, log, cancellationToken);
    }

    private static async Task<string> BuildMonoAsync(
        BuildContext ctx,
        string repoPath,
        ResolvedModulePackage modulePackage,
        BuildLogger log,
        CancellationToken cancellationToken
    )
    {
        var projectDir = ResolveMonoProjectDir(repoPath);
        await UnpackAsync(ctx, modulePackage, Path.Combine(projectDir, "Shared", "Managed"), log, cancellationToken);

        log.Status(Stage, $"building modules (Release, SptVersion={ctx.Version})...");
        await BuildAsync(
            ["build", "-c", "Release", $"-p:SptVersion={ctx.Version}"],
            projectDir,
            log,
            cancellationToken
        );
        return BuildDirOf(projectDir);
    }

    private static async Task<string> BuildIl2CppAsync(
        BuildContext ctx,
        string repoPath,
        ResolvedModulePackage modulePackage,
        BuildLogger log,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(Path.Combine(repoPath, "Modules.slnx")))
        {
            throw new StageFailedException(
                Stage,
                $"No modules solution found under {repoPath}.",
                "Expected Modules.slnx at the repo root."
            );
        }

        var gameDir = Path.Combine(ctx.OutputDir, ".gamedir");
        Fs.DeleteDirectory(gameDir);
        await UnpackAsync(ctx, modulePackage, gameDir, log, cancellationToken);
        foreach (var folder in Il2CppPackageFolders)
        {
            if (!Directory.Exists(Path.Combine(gameDir, folder)))
            {
                throw new StageFailedException(
                    Stage,
                    $"The module package has no {folder} folder.",
                    "A 5.x package holds BepInEx/interop and BepInEx/core, laid out like a game install."
                );
            }
        }

        log.Status(Stage, $"building modules (Release, SPTushonkaVersion={ctx.Version})...");
        await BuildAsync(
            [
                "build",
                Path.Combine("SPTushonka.Build", "SPTushonka.Build.csproj"),
                "-c",
                "Release",
                $"-p:SPTushonkaVersion={ctx.Version}",
                $"-p:GameDir={gameDir}",
            ],
            repoPath,
            log,
            cancellationToken
        );

        var buildDir = BuildDirOf(repoPath);
        Fs.DeleteDirectory(gameDir);
        return buildDir;
    }

    private static async Task UnpackAsync(
        BuildContext ctx,
        ResolvedModulePackage modulePackage,
        string target,
        BuildLogger log,
        CancellationToken cancellationToken
    )
    {
        if (modulePackage.IsDirectory)
        {
            log.Status(Stage, $"copying local module files into {Path.GetFileName(target)}...");
            Fs.CopyDirectory(modulePackage.Path, target);
            return;
        }

        // Extracts straight into the target, leaving no archive file behind in the tree.
        log.Status(Stage, $"extracting module package into {Path.GetFileName(target)}...");
        await ctx.Tools.SevenZip.ExtractAsync(
            modulePackage.Path,
            target,
            line => log.Line(Stage, line),
            cancellationToken
        );
    }

    private static async Task BuildAsync(
        string[] arguments,
        string workingDir,
        BuildLogger log,
        CancellationToken cancellationToken
    )
    {
        var exitCode = await ProcessRunner.RunAsync(
            "dotnet",
            arguments,
            workingDir,
            onLine: line => log.Line(Stage, line),
            cancellationToken: cancellationToken
        );
        if (exitCode != 0)
        {
            throw new StageFailedException(Stage, $"Modules build failed (exit {exitCode}).");
        }
    }

    private static string BuildDirOf(string projectDir)
    {
        var buildDir = Path.Combine(projectDir, "Build");
        if (!Directory.Exists(buildDir) || !Directory.EnumerateFileSystemEntries(buildDir).Any())
        {
            throw new StageFailedException(Stage, $"Modules build produced nothing at {buildDir}.");
        }

        return buildDir;
    }

    /// <summary>
    /// The directory the mono modules solution builds from. The projects were lifted out of project/ to the repo
    /// root, so both arrangements are probed by the Shared/ directory every tag has.
    /// </summary>
    private static string ResolveMonoProjectDir(string repoPath)
    {
        foreach (var candidate in new[] { repoPath, Path.Combine(repoPath, "project") })
        {
            if (Directory.Exists(Path.Combine(candidate, "Shared")))
            {
                return candidate;
            }
        }

        throw new StageFailedException(
            Stage,
            $"No modules project layout found under {repoPath}.",
            "Expected a Shared/ directory at the repo root or under project/."
        );
    }
}
