using System.ComponentModel;
using System.Text.RegularExpressions;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

/// <summary>
/// Reads a PDF's embedded text layer with Poppler's <c>pdftotext -layout</c>, which keeps column
/// alignment as whitespace — the gap PdfPig's <c>page.Text</c> leaves by flattening columns into prose.
/// One process per document; the output is split into pages on the form feeds Poppler emits, and each
/// page's padding is compacted (<see cref="CompactLayout"/>).
/// </summary>
public partial class PdfTextRunner(ILogger<PdfTextRunner> logger) : IPdfTextRunner
{
    // The runner is a singleton: a host without pdftotext would otherwise log the same warning for
    // every PDF it ever extracts.
    private int _missingLogged;

    internal static IReadOnlyList<string> BuildArguments(string filePath) =>
        ["-layout", "-enc", "UTF-8", filePath, "-"];

    /// <summary>
    /// Splits pdftotext's stdout into pages. Poppler writes a form feed after EVERY page, the last
    /// included, so only the trailing empty segment is dropped; interior empty segments are real
    /// text-less pages and must stay so page numbers line up with the PDF's.
    /// </summary>
    internal static IReadOnlyList<string> SplitPages(string stdout)
    {
        if (stdout.Length == 0)
            return [];

        var pages = stdout.Split('\f');
        return pages[^1].Length == 0 ? pages[..^1] : pages;
    }

    /// <summary>
    /// Collapses every run of 2+ spaces/tabs to exactly two and strips trailing whitespace per line.
    /// <c>-layout</c> pads columns to their printed x-position, which inflates text 1.1–2.3× on the repo's
    /// fixtures — and every consumer budgets characters (the AI statement and classification inputs) or
    /// tokens (MCP). Two spaces stays a clear column break, so what PdfPig lost (one line per row, columns
    /// kept apart) survives; only pixel-exact alignment in a monospace view is given up.
    /// </summary>
    internal static string CompactLayout(string page) =>
        TrailingWhitespace().Replace(HorizontalRun().Replace(page, "  "), "");

    [GeneratedRegex("[ \t]{2,}")]
    private static partial Regex HorizontalRun();

    [GeneratedRegex("[ \t]+$", RegexOptions.Multiline)]
    private static partial Regex TrailingWhitespace();

    public async Task<IReadOnlyList<string>?> ExtractPagesAsync(string filePath, CancellationToken ct)
    {
        ExternalProcessResult result;
        try
        {
            result = await ExternalProcess.RunAsync("pdftotext", BuildArguments(filePath), stdin: null, ct);
        }
        catch (Win32Exception ex)
        {
            if (Interlocked.Exchange(ref _missingLogged, 1) == 0)
                logger.LogWarning(ex,
                    "pdftotext could not be started (is poppler-utils installed?); PDF text layers will be read with PdfPig");
            return null;
        }

        if (result.ExitCode != 0)
        {
            logger.LogWarning("pdftotext exited with code {ExitCode} for {FilePath}: {StdErr}",
                result.ExitCode, filePath, result.StdErr);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(result.StdErr))
            logger.LogDebug("pdftotext stderr for {FilePath}: {StdErr}", filePath, result.StdErr);

        return SplitPages(result.StdOut).Select(CompactLayout).ToList();
    }
}
