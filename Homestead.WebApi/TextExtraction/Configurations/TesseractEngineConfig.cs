using Homestead.WebApi.Domains.Settings;

namespace Homestead.WebApi.TextExtraction.Configurations;

public record TesseractEngineConfig
{
    public TesseractLanguage Language { get; init; } = TesseractLanguage.Eng;
}
