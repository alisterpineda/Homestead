namespace Homestead.WebApi.Features.Documents.TextExtraction;

public class TesseractOptions
{
    public const string SectionName = "Ocr:Tesseract";

    public string TessDataPath { get; set; } = "tessdata";
}
