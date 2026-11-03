using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

/// <summary>What an external tool printed and how it exited.</summary>
public sealed record ExternalProcessResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Runs a command-line tool to completion — the one place the extraction runners start processes, so the
/// three things that are easy to get wrong are got right once:
/// <list type="bullet">
/// <item>stdout and stderr are drained <b>concurrently</b>; reading one after the other deadlocks as soon
/// as the unread one fills its pipe buffer (a chatty stderr is enough);</item>
/// <item>stdin, when given, is written while both are being drained, for the same reason;</item>
/// <item>cancellation <b>kills the process tree</b> before rethrowing, so a timed-out or shut-down job does
/// not leave the tool running with nobody waiting for it.</item>
/// </list>
/// </summary>
/// <remarks>
/// A missing binary surfaces as the <see cref="Win32Exception"/> from <see cref="Process.Start()"/>, untouched:
/// whether that is fatal (tesseract) or a fallback (pdftotext) is the caller's decision. A non-zero exit is
/// returned, not thrown, for the same reason.
/// </remarks>
internal static class ExternalProcess
{
    public static async Task<ExternalProcessResult> RunAsync(
        string fileName, IEnumerable<string> arguments, Stream? stdin, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in arguments)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            if (stdin is not null)
                await WriteStdinAsync(process, stdin, ct);

            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(ct);

            return new ExternalProcessResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
    }

    private static async Task WriteStdinAsync(Process process, Stream stdin, CancellationToken ct)
    {
        try
        {
            await stdin.CopyToAsync(process.StandardInput.BaseStream, ct);
        }
        catch (IOException)
        {
            // The tool closed its end before taking all the input — it is exiting, typically because it
            // rejected what it was given. Its exit code and stderr say why far better than "broken pipe".
        }
        finally
        {
            try { process.StandardInput.Close(); }
            catch (IOException) { }
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited, or exiting: nothing left to stop.
        }
    }
}
