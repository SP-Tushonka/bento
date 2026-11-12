namespace Bento.Steps;

public static class NodeServerStep
{
    public const string Stage = "server";

    public static async Task<ServerArtifacts> RunAsync(
        BuildContext ctx,
        BuildLogger log,
        CancellationToken cancellationToken = default
    )
    {
        var projectDir = Path.Combine(ctx.Server!.Path, "project");
        if (!Directory.Exists(projectDir))
        {
            throw new StageFailedException(Stage, $"The server repo has no project/ directory: {projectDir}");
        }

        var toolchain = NodeToolchain.Resolve(projectDir);
        await VerifyNodeAsync(toolchain, log);

        var (asOf, dated) = await DependencyDateAsync(ctx.Server.Path);
        log.Status(Stage, $"installing npm dependencies (node {toolchain.Version}, as of {asOf} from {dated})...");
        await RunNpmAsync(
            toolchain,
            ["install", $"--before={asOf}"],
            projectDir,
            "npm install",
            log,
            cancellationToken
        );

        var task = ctx.BuildType.ToNodeBuildTask();
        log.Status(Stage, $"building {task} for win32-x64...");
        var gulpfile = Path.Combine(projectDir, "gulpfile.mjs");
        var original = await File.ReadAllTextAsync(gulpfile, cancellationToken);
        await File.WriteAllTextAsync(gulpfile, WithoutBytecode(original, gulpfile), cancellationToken);
        try
        {
            await RunNpmAsync(
                toolchain,
                ["run", task, "--", "--arch=x64", "--platform=win32"],
                projectDir,
                task,
                log,
                cancellationToken
            );
        }
        finally
        {
            await File.WriteAllTextAsync(gulpfile, original, CancellationToken.None);
        }

        var buildDir = Path.Combine(projectDir, "build");
        foreach (var expected in new[] { "SPT.Server.exe", "SPT_Data" })
        {
            if (!Path.Exists(Path.Combine(buildDir, expected)))
            {
                throw new StageFailedException(
                    Stage,
                    $"Expected server output missing: {Path.Combine(buildDir, expected)}"
                );
            }
        }

        return new ServerArtifacts([buildDir]);
    }

    // pkg's bytecode comes from the Linux Node bento runs. The Windows Node 22 of 3.11 rejects it and exits with code 4.
    private static string WithoutBytecode(string gulpfile, string path)
    {
        if (gulpfile.Contains("--no-bytecode", StringComparison.Ordinal))
        {
            return gulpfile;
        }

        const string output = "\"--output\",";
        var at = gulpfile.IndexOf(output, StringComparison.Ordinal);
        if (at < 0)
        {
            throw new StageFailedException(Stage, $"Could not find the pkg arguments in {path}.");
        }

        return gulpfile.Insert(at, "\"--no-bytecode\", \"--public-packages\", \"*\", \"--public\", ");
    }

    private static async Task VerifyNodeAsync(NodeToolchain toolchain, BuildLogger log)
    {
        var (exitCode, stdOut, stdErr) = await ProcessRunner.CaptureAsync(toolchain.NodePath, ["--version"]);
        if (exitCode != 0)
        {
            throw new StageFailedException(
                Stage,
                $"Node {toolchain.Version} is not available: {stdErr.Trim()}",
                $"Install it under {NodeToolchain.InstallRoot}/{toolchain.Version} or put it on PATH."
            );
        }

        var found = stdOut.Trim().TrimStart('v', 'V');
        var wantedMajor = toolchain.Version[..toolchain.Version.IndexOf('.')];
        if (!found.StartsWith($"{wantedMajor}.", StringComparison.Ordinal))
        {
            throw new StageFailedException(
                Stage,
                $"This tag pins Node {toolchain.Version} but {found} is in use.",
                $"Install Node {toolchain.Version} under {NodeToolchain.InstallRoot}/{toolchain.Version}."
            );
        }

        log.Line(Stage, $"node {found}");
    }

    private static async Task RunNpmAsync(
        NodeToolchain toolchain,
        string[] arguments,
        string workingDirectory,
        string what,
        BuildLogger log,
        CancellationToken cancellationToken
    )
    {
        var exitCode = await ProcessRunner.RunAsync(
            toolchain.NpmPath,
            arguments,
            workingDirectory,
            toolchain.Environment(),
            line => log.Line(Stage, line),
            cancellationToken
        );
        if (exitCode != 0)
        {
            throw new StageFailedException(Stage, $"Server {what} failed (exit {exitCode}).");
        }
    }

    private static async Task<(string AsOf, string Revision)> DependencyDateAsync(string repoPath)
    {
        var (described, tag, _) = await ProcessRunner.CaptureAsync(
            "git",
            ["describe", "--tags", "--abbrev=0"],
            repoPath
        );
        var revision = described == 0 && tag.Trim() is { Length: > 0 } found ? found : "HEAD";

        var (exitCode, stdOut, stdErr) = await ProcessRunner.CaptureAsync(
            "git",
            ["show", "-s", "--format=%cs", $"{revision}^{{commit}}"],
            repoPath
        );
        if (exitCode != 0 || stdOut.Trim().Length == 0)
        {
            throw new StageFailedException(Stage, $"Could not read the date of {revision}: {stdErr.Trim()}");
        }

        return (stdOut.Trim(), revision);
    }
}
