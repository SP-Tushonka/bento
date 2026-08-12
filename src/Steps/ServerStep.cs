namespace Bento.Steps;

/// <summary>
/// The server's win-x64 and linux-x64 publish output directories.
/// </summary>
public sealed record ServerArtifacts(string WinPublishDir, string LinuxPublishDir);

/// <summary>
/// Publishes the server for win-x64 and linux-x64.
/// </summary>
public static class ServerStep
{
    public const string Stage = "server";

    private static readonly string[] Platforms = ["linux-x64", "win-x64"];

    /// <summary>
    /// Publishes the server once per platform with the version/build metadata stamped in, then returns the two publish
    /// directories after confirming they exist.
    /// </summary>
    public static async Task<ServerArtifacts> RunAsync(
        BuildContext ctx,
        BuildLogger log,
        CancellationToken cancellationToken = default
    )
    {
        var repo = ctx.Server!;
        var layout = ServerLayout.Detect(repo.Path);

        // Publishes platforms one at a time (both share the obj/ directory, so concurrent publishes race).
        foreach (var platform in Platforms)
        {
            log.Status(Stage, $"publishing {platform} ({ctx.BuildConfig})...");
            string[] arguments =
            [
                "publish",
                layout.ServerProjectFile,
                "-c",
                ctx.BuildConfig,
                "-f",
                "net10.0",
                "-r",
                platform,
                "-p:IncludeNativeLibrariesForSelfExtract=true",
                "-p:PublishSingleFile=false",
                "--self-contained",
                "false",
                $"-p:SptBuildType={ctx.BuildTypeProperty}",
                $"-p:SptVersion={ctx.Version}",
                $"-p:SptBuildTime={ctx.BuildTimeUtc:yyyyMMdd}",
                $"-p:SptCommit={repo.Commit}",
                "-p:IsPublish=true",
            ];
            var exitCode = await ProcessRunner.RunAsync(
                "dotnet",
                arguments,
                repo.Path,
                onLine: line => log.Line(Stage, line),
                cancellationToken: cancellationToken
            );
            if (exitCode != 0)
            {
                throw new StageFailedException(Stage, $"Server publish for {platform} failed (exit {exitCode}).");
            }
        }

        var artifacts = new ServerArtifacts(
            layout.PublishDir(ctx.BuildConfig, "win-x64"),
            layout.PublishDir(ctx.BuildConfig, "linux-x64")
        );
        foreach (var directory in new[] { artifacts.WinPublishDir, artifacts.LinuxPublishDir })
        {
            if (!Directory.Exists(directory))
            {
                throw new StageFailedException(Stage, $"Expected publish output missing: {directory}");
            }
        }

        return artifacts;
    }
}
