namespace Homestead.WebApi.Infrastructure.TextExtraction;

public class TesseractOptions
{
    public const string SectionName = "Ocr:Tesseract";

    public string TessDataPath { get; set; } = "tessdata";
}
