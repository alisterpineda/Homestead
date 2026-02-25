namespace HomeSuiteHome.WebApi.TextExtraction;

public class TesseractOptions
{
    public const string SectionName = "Ocr:Tesseract";

    public string TessDataPath { get; set; } = "tessdata";
    public string Language { get; set; } = "eng";
}
