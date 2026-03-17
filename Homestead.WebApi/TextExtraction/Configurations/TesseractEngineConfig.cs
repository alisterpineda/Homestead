using Homestead.WebApi.Domains.Settings;

namespace Homestead.WebApi.TextExtraction.Configurations;

public record TesseractEngineConfig
{
    public List<TesseractLanguage> Languages { get; init; } = [TesseractLanguage.Eng];
}
