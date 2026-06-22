using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

public class TesseractOptions
{
    public const string SectionName = "Ocr:Tesseract";

    public string TessDataPath { get; set; } = "tessdata";

    // OCR languages, joined into Tesseract's "eng+fra" form at extraction time. Absorbs what the
    // per-engine {"languages":[...]} JSON used to carry; defaults to English when unset.
    public List<TesseractLanguage> Languages { get; set; } = [TesseractLanguage.Eng];
}
