namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Helpers for invoking external bioinformatics CLI tools (bwa, freebayes, samtools, bcl-convert)
/// inside BenchmarkDotNet iterations.
///
/// Design notes
/// ─────────────
/// • Each call spawns a new process so the measurement includes process-start overhead,
///   which mirrors real-world CLI usage and is fair to BioSharp (which pays .NET JIT once
///   at startup and then runs warm for subsequent iterations).
/// • stdout is redirected to /dev/null (or NUL on Windows) so I/O doesn't dominate.
/// • stderr is captured only when the process exits non-zero, to aid debugging.
/// • <see cref="FindTool"/> resolves the tool on PATH; when not found the benchmark
///   method checks <see cref="IsAvailable"/> and returns immediately so BenchmarkDotNet
///   still records a result (0 or a sentinel) rather than throwing.
/// </summary>
public static class ExternalProcess
{
    /// <summary>Returns true when <paramref name="toolName"/> is resolvable on PATH.</summary>
    public static bool IsAvailable(string toolName)
    {
        return FindTool(toolName) != null;
    }

    /// <summary>
    /// Resolves the full path to <paramref name="toolName"/> by searching PATH.
    /// Returns null when not found.
    /// </summary>
    public static string? FindTool(string toolName)
    {
        // On unix try `which`; on windows try `where`
        var whichCmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";
        try
        {
            var psi = new ProcessStartInfo(whichCmd, toolName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var p = Process.Start(psi)!;
            var line = p.StandardOutput.ReadLine();
            p.WaitForExit(2_000);
            if (!string.IsNullOrWhiteSpace(line) && File.Exists(line.Trim()))
            {
                return line.Trim();
            }
        }
        catch
        {
            // ignore — tool is not available
        }

        return null;
    }

    /// <summary>
    /// Runs an external command, waits for it to complete, and returns the exit code.
    /// stdout is discarded; stderr is optionally captured.
    ///
    /// Throws <see cref="InvalidOperationException"/> if the process cannot be started.
    /// Does NOT throw on non-zero exit code — callers should check the return value.
    /// </summary>
    public static int Run(
        string executable,
        string arguments,
        string? workingDirectory = null,
        int timeoutMs = 120_000)
    {
        var psi = new ProcessStartInfo(executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {executable}");
        // Drain stdout/stderr to avoid blocking.
        // Attach handlers before Begin*ReadLine so no early output is missed.
        p.OutputDataReceived += (_, _) => { };
        p.ErrorDataReceived  += (_, _) => { };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (!p.WaitForExit(timeoutMs))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"{executable} did not complete within {timeoutMs} ms");
        }

        return p.ExitCode;
    }

    /// <summary>
    /// Same as <see cref="Run"/> but captures stderr and returns it alongside the exit code.
    /// Useful for publish / build steps where the error output aids diagnostics.
    /// </summary>
    public static (int ExitCode, string Stderr) RunCapture(
        string executable,
        string arguments,
        string? workingDirectory = null,
        int timeoutMs = 120_000)
    {
        var psi = new ProcessStartInfo(executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        var sb = new StringBuilder();
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {executable}");
        p.OutputDataReceived += (_, _) => { };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null)
            {
                sb.AppendLine(e.Data);
            }
        };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (!p.WaitForExit(timeoutMs))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"{executable} did not complete within {timeoutMs} ms");
        }

        return (p.ExitCode, sb.ToString());
    }

    /// <summary>
    /// Same as <see cref="Run"/> but returns elapsed wall-clock time in milliseconds.
    /// </summary>
    public static (int ExitCode, long ElapsedMs) RunTimed(
        string executable,
        string arguments,
        string? workingDirectory = null,
        int timeoutMs = 120_000)
    {
        var sw = Stopwatch.StartNew();
        var exit = Run(executable, arguments, workingDirectory, timeoutMs);
        sw.Stop();
        return (exit, sw.ElapsedMilliseconds);
    }

    /// <summary>Null-output device path for the current platform.</summary>
    public static string NullDevice =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "NUL" : "/dev/null";

    /// <summary>
    /// Convenience wrapper: run a shell command string via sh/cmd.
    /// Useful for pipelines like "bwa mem ref.fa reads.fq | samtools sort -o out.bam".
    /// </summary>
    public static int Shell(string command, string? workingDirectory = null, int timeoutMs = 120_000)
    {
        var (shell, flag) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("cmd.exe", "/c")
            : ("/bin/sh", "-c");

        return Run(shell, $"{flag} \"{command}\"", workingDirectory, timeoutMs);
    }
}


