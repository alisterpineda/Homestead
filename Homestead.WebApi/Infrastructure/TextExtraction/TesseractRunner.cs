using System.Diagnostics;
using System.Globalization;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

public class TesseractRunner(ILogger<TesseractRunner> logger) : ITesseractRunner
{
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

    public async Task<string?> OcrAsync(Stream imageStream, TesseractRunOptions options, CancellationToken ct)
    {
        logger.LogDebug("Running tesseract OCR with tessdata={TessDataPath}, lang={Language}",
            options.TessDataPath, options.Language);

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

        var args = BuildArguments(
            options.Language, options.TessDataPath, options.Dpi, options.Psm, options.Oem);
        foreach (var arg in args)
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
