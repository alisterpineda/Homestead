using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

public class TesseractRunner(IOptions<TesseractOptions> options, ILogger<TesseractRunner> logger) : ITesseractRunner
{
    private readonly string _tessDataPath = options.Value.TessDataPath;
    private readonly int _psm = options.Value.Psm;
    private readonly int _oem = options.Value.Oem;

    internal static IReadOnlyList<string> BuildArguments(
        string language, string tessDataPath, int? dpi, int psm, int oem)
    {
        var args = new List<string>
        {
            "stdin", "stdout",
            "-l", language,
            "--tessdata-dir", tessDataPath,
            "--psm", psm.ToString(CultureInfo.InvariantCulture),
            "--oem", oem.ToString(CultureInfo.InvariantCulture),
        };
        if (dpi is { } d)
        {
            args.Add("--dpi");
            args.Add(d.ToString(CultureInfo.InvariantCulture));
        }
        return args;
    }

    public async Task<string?> OcrAsync(Stream imageStream, string language, int? dpi, CancellationToken ct)
    {
        logger.LogDebug("Running tesseract OCR with tessdata={TessDataPath}, lang={Language}",
            _tessDataPath, language);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "tesseract",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in BuildArguments(language, _tessDataPath, dpi, _psm, _oem))
            process.StartInfo.ArgumentList.Add(arg);

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
