using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace HomeSuiteHome.WebApi.TextExtraction;

public class TesseractTextExtractor(
    IOptions<TesseractOptions> options,
    ILogger<TesseractTextExtractor> logger) : ITextExtractor
{
    private static readonly HashSet<string> SupportedContentTypes =
    [
        "image/png",
        "image/jpeg",
        "image/tiff",
        "image/bmp",
        "image/gif"
    ];

    private readonly string _tessDataPath = options.Value.TessDataPath;
    private readonly string _language = options.Value.Language;

    public bool CanExtract(string contentType)
        => SupportedContentTypes.Contains(contentType);

    public async Task<string?> ExtractTextAsync(string filePath, CancellationToken ct)
    {
        logger.LogDebug("Running tesseract on {FilePath} with tessdata={TessDataPath}, lang={Language}",
            filePath, _tessDataPath, _language);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "tesseract",
            ArgumentList = { "stdin", "stdout", "-l", _language, "--tessdata-dir", _tessDataPath },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        // Pipe file content to stdin to avoid subprocess file-access issues
        await using (var fileStream = File.OpenRead(filePath))
        {
            await fileStream.CopyToAsync(process.StandardInput.BaseStream, ct);
        }
        process.StandardInput.Close();

        var text = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            logger.LogWarning("Tesseract exited with code {ExitCode} for {FilePath}: {StdErr}",
                process.ExitCode, filePath, stderr);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(stderr))
            logger.LogDebug("Tesseract stderr for {FilePath}: {StdErr}", filePath, stderr);

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
