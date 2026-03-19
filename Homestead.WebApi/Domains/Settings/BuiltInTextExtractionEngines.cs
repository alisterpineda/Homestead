namespace Homestead.WebApi.Domains.Settings;

public static class BuiltInTextExtractionEngines
{
    public static readonly Guid TesseractDefaultId = new("00000000-0000-0000-0000-000000000001");
    public const string TesseractDefaultName = "Tesseract (Default)";
    public const string TesseractDefaultConfiguration = """{"languages":["Eng"]}""";
}
