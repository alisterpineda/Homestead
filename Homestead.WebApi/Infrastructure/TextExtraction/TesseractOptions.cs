using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

public class TesseractOptions
{
    public const string SectionName = "Ocr:Tesseract";

    public string TessDataPath { get; set; } = "tessdata";

    // OCR languages, joined into Tesseract's "eng+fra" form at extraction time. Absorbs what the
    // per-engine {"languages":[...]} JSON used to carry; defaults to English when unset.
    public List<TesseractLanguage> Languages { get; set; } = [TesseractLanguage.Eng];

    // DPI used to rasterize PDF pages, and passed as --dpi for those rendered pages.
    public int Dpi { get; set; } = 300;

    // Tesseract page segmentation mode (--psm). 3 = fully automatic, correct for multi-block documents.
    public int Psm { get; set; } = 3;

    // Tesseract OCR engine mode (--oem). 3 = default (best engine available); never errors on engine selection.
    public int Oem { get; set; } = 3;
}
