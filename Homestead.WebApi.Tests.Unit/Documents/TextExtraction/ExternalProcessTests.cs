using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Homestead.WebApi.Infrastructure.TextExtraction;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

// Runs real processes through /bin/sh — the helper's whole job is process plumbing, which a substitute
// cannot exercise. Linux-only, like the tools it starts (tesseract, pdftotext).
public class ExternalProcessTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static Task<ExternalProcessResult> Sh(string script, Stream? stdin = null, CancellationToken? ct = null) =>
        ExternalProcess.RunAsync("/bin/sh", ["-c", script], stdin, ct ?? Ct);

    [Fact]
    public async Task RunAsync_CapturesStdoutStderrAndExitCode()
    {
        var result = await Sh("printf 'out'; printf 'err' >&2; exit 3");

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("out", result.StdOut);
        Assert.Equal("err", result.StdErr);
    }

    [Fact]
    public async Task RunAsync_WritesStdinToTheProcess()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("piped through"));

        var result = await Sh("cat", input);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("piped through", result.StdOut);
    }

    // Reading stdout to the end before touching stderr deadlocks once the tool fills the stderr pipe
    // (64 KiB on Linux) while we wait on stdout. Well past that, both must come back.
    [Fact]
    public async Task RunAsync_ChattyStderr_DoesNotDeadlock()
    {
        var result = await Sh("head -c 1048576 /dev/zero | tr '\\0' e >&2; printf done")
            .WaitAsync(TimeSpan.FromSeconds(20), Ct);

        Assert.Equal("done", result.StdOut);
        Assert.Equal(1048576, result.StdErr.Length);
    }

    // Big input against a tool that writes as it reads: stdin must be fed while stdout is drained.
    [Fact]
    public async Task RunAsync_LargeStdinAndStdout_DoesNotDeadlock()
    {
        using var input = new MemoryStream(new byte[2 * 1024 * 1024].Select(_ => (byte)'x').ToArray());

        var result = await Sh("cat", input).WaitAsync(TimeSpan.FromSeconds(20), Ct);

        Assert.Equal(2 * 1024 * 1024, result.StdOut.Length);
    }

    // A tool that exits without reading its input is reported by its exit code, not as a broken pipe.
    [Fact]
    public async Task RunAsync_ToolIgnoresStdin_ReturnsItsExitCode()
    {
        using var input = new MemoryStream(new byte[4 * 1024 * 1024]);

        var result = await Sh("exec 0<&-; printf 'bad input' >&2; exit 1", input);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("bad input", result.StdErr);
    }

    [Fact]
    public async Task RunAsync_MissingBinary_ThrowsWin32Exception()
    {
        await Assert.ThrowsAsync<Win32Exception>(() =>
            ExternalProcess.RunAsync("homestead-no-such-tool", [], stdin: null, Ct));
    }

    // Cancelling must not leave the tool running for nobody: the process (and its children) is killed.
    [Fact]
    public async Task RunAsync_Cancelled_KillsTheProcessAndRethrows()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var pidFile = Path.Combine(Path.GetTempPath(), $"external-process-{Guid.NewGuid():N}.pid");

        try
        {
            var run = Sh($"echo $$ > '{pidFile}'; exec sleep 60", ct: cts.Token);

            await WaitForAsync(() => File.Exists(pidFile) && new FileInfo(pidFile).Length > 0);
            var pid = int.Parse((await File.ReadAllTextAsync(pidFile, Ct)).Trim());

            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

            await WaitForAsync(() => !IsRunning(pid));
        }
        finally
        {
            File.Delete(pidFile);
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            await Task.Delay(20, Ct);
        }
    }
}
