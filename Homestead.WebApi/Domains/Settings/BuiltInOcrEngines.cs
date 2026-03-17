namespace Homestead.WebApi.Domains.Settings;

public static class BuiltInOcrEngines
{
    public static readonly Guid TesseractDefaultId = new("00000000-0000-0000-0000-000000000001");
    public const string TesseractDefaultName = "Tesseract (English)";
    public const string TesseractDefaultConfiguration = """{"language":"Eng"}""";
}
