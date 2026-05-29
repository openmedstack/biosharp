namespace OpenMedStack.BioSharp.AcceptanceTests;

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Thin wrapper around external bioinformatics CLI tools used in the equivalency
/// acceptance tests.  Mirrors the <c>ExternalProcess</c> helper in the benchmarks
/// project but is scoped to the test assembly so the projects remain independent.
/// </summary>
public static class ExternalToolRunner
{
    /// <summary>Returns <c>true</c> when <paramref name="toolName"/> is resolvable on PATH.</summary>
    public static bool IsAvailable(string toolName)
    {
        return FindTool(toolName) != null;
    }

    /// <summary>Resolves the full path of <paramref name="toolName"/> via PATH, or returns null.</summary>
    public static string? FindTool(string toolName)
    {
        var whichCmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";
        try
        {
            var psi = new ProcessStartInfo(whichCmd, toolName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
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
            // tool not on PATH
        }

        return null;
    }

    /// <summary>
    /// Runs an external command and waits for it to complete.
    /// Returns the exit code.  stdout/stderr are drained to avoid blocking.
    /// </summary>
    public static int Run(
        string executable,
        string arguments,
        string? workingDirectory = null,
        int timeoutMs = 180_000)
    {
        var psi = new ProcessStartInfo(executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {executable}");
        p.OutputDataReceived += (_, _) => { };
        p.ErrorDataReceived += (_, _) => { };
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
    /// Runs an external command and captures its stdout.
    /// Returns the exit code and captured stdout text.
    /// </summary>
    public static (int ExitCode, string Stdout) RunCapturingStdout(
        string executable,
        string arguments,
        string? workingDirectory = null,
        int timeoutMs = 180_000)
    {
        var psi = new ProcessStartInfo(executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        var stdoutBuilder = new StringBuilder();
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {executable}");

        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stdoutBuilder.AppendLine(e.Data);
            }
        };
        p.ErrorDataReceived += (_, _) => { };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (!p.WaitForExit(timeoutMs))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"{executable} did not complete within {timeoutMs} ms");
        }

        return (p.ExitCode, stdoutBuilder.ToString());
    }

    /// <summary>
    /// Runs a shell pipeline (e.g. <c>bwa mem ... | samtools sort ...</c>).
    /// Uses <c>/bin/sh -c &lt;command&gt;</c> on Unix and <c>cmd.exe /c &lt;command&gt;</c> on Windows.
    ///
    /// <para>
    /// The command string is passed as a single argv element so that shell operators
    /// (pipes, redirects, quoted paths) are interpreted correctly by the shell.
    /// Using <see cref="ProcessStartInfo.ArgumentList"/> avoids the double-quoting
    /// ambiguity that arises when wrapping the entire command in an extra pair of
    /// quotes and relying on <see cref="ProcessStartInfo.Arguments"/> string splitting.
    /// </para>
    /// </summary>
    public static int Shell(string command, string? workingDirectory = null, int timeoutMs = 180_000)
    {
        var (shell, flag) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("cmd.exe", "/c")
            : ("/bin/sh", "-c");

        var psi = new ProcessStartInfo(shell)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(flag);
        psi.ArgumentList.Add(command);
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {shell}");
        p.OutputDataReceived += (_, _) => { };
        p.ErrorDataReceived += (_, _) => { };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (!p.WaitForExit(timeoutMs))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"Shell command did not complete within {timeoutMs} ms");
        }

        return p.ExitCode;
    }

    /// <summary>Runs a shell command and captures stdout.</summary>
    public static (int ExitCode, string Stdout) ShellCapture(string command, string? workingDirectory = null, int timeoutMs = 180_000)
    {
        var (shell, flag) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("cmd.exe", "/c")
            : ("/bin/sh", "-c");

        var psi = new ProcessStartInfo(shell)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(flag);
        psi.ArgumentList.Add(command);
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        var stdoutBuilder = new StringBuilder();
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {shell}");

        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stdoutBuilder.AppendLine(e.Data);
            }
        };
        p.ErrorDataReceived += (_, _) => { };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (!p.WaitForExit(timeoutMs))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"Shell command did not complete within {timeoutMs} ms");
        }

        return (p.ExitCode, stdoutBuilder.ToString());
    }

    /// <summary>Null device path for the current OS.</summary>
    public static string NullDevice =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "NUL" : "/dev/null";
}
