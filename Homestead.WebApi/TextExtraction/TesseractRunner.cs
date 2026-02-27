using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.TextExtraction;

public class TesseractRunner(IOptions<TesseractOptions> options, ILogger<TesseractRunner> logger)
{
    private readonly string _tessDataPath = options.Value.TessDataPath;
    private readonly string _language = options.Value.Language;

    public async Task<string?> OcrAsync(Stream imageStream, CancellationToken ct)
    {
        logger.LogDebug("Running tesseract OCR with tessdata={TessDataPath}, lang={Language}",
            _tessDataPath, _language);

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

        await imageStream.CopyToAsync(process.StandardInput.BaseStream, ct);
        process.StandardInput.Close();

        var text = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            logger.LogWarning("Tesseract exited with code {ExitCode}: {StdErr}",
                process.ExitCode, stderr);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(stderr))
            logger.LogDebug("Tesseract stderr: {StdErr}", stderr);

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
