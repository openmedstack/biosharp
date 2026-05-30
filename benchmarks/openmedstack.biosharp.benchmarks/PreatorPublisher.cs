using System.Collections.Generic;
using System.Linq;

namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.IO;

/// <summary>
/// Publishes the preator CLI application once per process and caches the result.
///
/// Design notes
/// ─────────────
/// • Publish is done with <c>dotnet publish -c Release --self-contained false</c> so the output
///   mirrors a real framework-dependent deployment (same as installing a .NET tool globally).
/// • <c>PublishTrimmed</c> is overridden to <c>false</c> for the benchmark publish because
///   trimming + <c>--self-contained false</c> is not supported and would fail.
/// • The DLL is invoked as <c>dotnet preator.dll &lt;subcommand&gt; &lt;args&gt;</c>, which
///   is exactly how a framework-dependent publish of a console app is called in CI/CD.
/// • A static double-checked lock ensures the expensive publish step runs at most once
///   even when multiple benchmark classes call <see cref="GetPreatorDll"/> in their
///   [GlobalSetup] methods.
/// </summary>
public static class PreatorPublisher
{
    private static readonly object Lock = new();
    private static string? _preatorDllPath;
    private static string? _publishError;
    private static bool _publishAttempted;

    /// <summary>
    /// Returns the absolute path to <c>preator.dll</c> in the publish output directory,
    /// or <c>null</c> if publishing failed (see <see cref="GetPublishError"/>).
    /// </summary>
    public static string? GetPreatorDll()
    {
        EnsurePublished();
        return _preatorDllPath;
    }

    /// <summary>
    /// Returns the human-readable error from a failed publish, or <c>null</c> when successful.
    /// </summary>
    public static string? GetPublishError()
    {
        EnsurePublished();
        return _publishError;
    }

    /// <summary>
    /// Runs the published preator binary with the given subcommand and arguments.
    /// Throws <see cref="InvalidOperationException"/> when the binary is not available.
    /// Returns the exit code.
    /// </summary>
    public static int Run(string preatorArguments, string? workingDirectory = null, int timeoutMs = 300_000)
    {
        var dll = GetPreatorDll()
         ?? throw new InvalidOperationException(
                $"preator is not published and cannot be benchmarked: {GetPublishError()}");
        var runningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        return runningInContainer
            ? ExternalProcess.Run("/app/preator/preator", $"\"{dll}\" {preatorArguments}", workingDirectory, timeoutMs)
            : ExternalProcess.Run("dotnet", $"\"{dll}\" {preatorArguments}", workingDirectory, timeoutMs);
    }

    // ── Private implementation ────────────────────────────────────────────────

    private static void EnsurePublished()
    {
        lock (Lock)
        {
            if (_publishAttempted)
            {
                return;
            }

            _publishAttempted = true;
            try
            {
                const string dockerPath = "/app/preator/preator";
                _preatorDllPath = File.Exists(dockerPath) ? dockerPath : Publish();
            }
            catch (Exception ex)
            {
                _publishError = ex.Message;
            }
        }
    }

    private static string Publish()
    {
        var repoRoot = FindRepoRoot()
         ?? throw new InvalidOperationException(
                "Cannot locate the repository root. " +
                "Expected to find 'openmedstack-biosharp.sln' and a 'data/' directory while walking up " +
                "from the application base directory or the current working directory.");

        var projectPath = Path.Combine(
            repoRoot, "src", "openmedstack.preator", "openmedstack.preator.csproj");

        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException(
                $"preator project file not found at the expected path: {projectPath}", projectPath);
        }

        var publishDir = Path.Combine(Path.GetTempPath(), $"preator-publish-{Guid.NewGuid():N}");
        Directory.CreateDirectory(publishDir);

        // Publish as a framework-dependent deployment (no self-contained bundle).
        // PublishTrimmed=false is mandatory: the csproj sets PublishTrimmed=true but EF Core is not
        // trim-compatible, so the trimmer produces fatal IL2104/NETSDK1144 errors unless overridden.
        var (exit, stderr) = ExternalProcess.RunCapture(
            "dotnet",
            $"publish \"{projectPath}\" -c Release -o \"{publishDir}\" --self-contained false /p:PublishReadyToRunShowWarnings=true /p:PublishTrimmed=false",
            workingDirectory: repoRoot,
            timeoutMs: 600_000);

        if (exit != 0)
        {
            var snippet = stderr.Length <= 4096 ? stderr : stderr[^4096..];
            throw new InvalidOperationException(
                $"dotnet publish exited with non-zero exit code {exit}. " +
                $"Check that the preator project builds cleanly. Publish output dir: {publishDir}\n" +
                $"Build output:\n{snippet}");
        }

        var dllPath = Path.Combine(publishDir, "preator.dll");
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException(
                $"dotnet publish succeeded but 'preator.dll' was not found in the output directory: {publishDir}",
                dllPath);
        }

        return dllPath;
    }

    private static string? FindRepoRoot()
    {
        List<DirectoryInfo?> dirs = [];
        foreach (var startDir in new[] { "/src", AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(Path.GetFullPath(startDir));
                dir != null;
                dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "openmedstack-biosharp.sln")))
                {
                    return dir.FullName;
                }

                dirs.Add(dir);
            }
        }

        throw new FileNotFoundException(
            "Repository root not found. Expected to find 'openmedstack-biosharp.sln' while walking up " +
            "from the application base directory or the current working directory. Ended at: " +
            string.Join(", ", dirs.Select(d => d?.FullName ?? "<null>")));
    }
}
