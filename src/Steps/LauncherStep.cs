namespace Bento.Steps;

/// <summary>
/// Publishes the launcher once per runtime.
/// </summary>
public static class LauncherStep
{
    public const string Stage = "launcher";

    private static readonly string[] Platforms = ["win-x64", "linux-x64"];

    /// <summary>
    /// Publishes the launcher per runtime, copies the Windows exe back into Build (which the Linux publish wiped), and
    /// returns Build after verifying both binaries and SPT_Data.
    /// </summary>
    public static async Task<string> RunAsync(
        BuildContext ctx,
        BuildLogger log,
        CancellationToken cancellationToken = default
    )
    {
        var repo = ctx.Launcher!;
        var layout = LauncherLayout.Detect(repo.Path);
        var projectDir = layout.ProjectRoot;

        // Publishes one platform at a time; the publishes share obj/ and each one's SPTBuildEvent wipes Build.
        foreach (var platform in Platforms)
        {
            log.Status(Stage, $"publishing {platform} (Release)...");
            string[] arguments =
            [
                "publish",
                layout.CsprojRelative,
                "-c",
                "Release",
                "--self-contained",
                "false",
                "-f",
                "net10.0",
                "-r",
                platform,
                "-p:PublishSingleFile=true",
                $"-p:SptVersion={ctx.Version}",
                // Without this the launcher's Build.props falls back to its placeholder, shipping a fake commit hash
                // in InformationalVersion.
                $"-p:SptCommitTag={repo.Commit}",
            ];
            var exitCode = await ProcessRunner.RunAsync(
                "dotnet",
                arguments,
                projectDir,
                onLine: line => log.Line(Stage, line),
                cancellationToken: cancellationToken
            );
            if (exitCode != 0)
            {
                throw new StageFailedException(Stage, $"Launcher publish for {platform} failed (exit {exitCode}).");
            }
        }

        // The Linux publish wipes Build (and the Windows exe). Copy the exe back in from its own publish directory.
        var buildDir = layout.BuildDir;
        var winExe = layout.PublishedExe("win-x64", "SPT.Launcher.exe");
        if (!File.Exists(winExe))
        {
            throw new StageFailedException(Stage, $"Windows launcher exe missing: {winExe}");
        }

        Directory.CreateDirectory(buildDir);
        File.Copy(winExe, Path.Combine(buildDir, "SPT.Launcher.exe"), overwrite: true);

        foreach (var expected in new[] { "SPT.Launcher.exe", "SPT.Launcher.Linux" })
        {
            if (!File.Exists(Path.Combine(buildDir, expected)))
            {
                throw new StageFailedException(
                    Stage,
                    $"Expected launcher output missing: {Path.Combine(buildDir, expected)}"
                );
            }
        }

        if (!Directory.Exists(Path.Combine(buildDir, "SPT_Data")))
        {
            throw new StageFailedException(Stage, $"Expected launcher SPT_Data missing under {buildDir}.");
        }

        return buildDir;
    }
}
