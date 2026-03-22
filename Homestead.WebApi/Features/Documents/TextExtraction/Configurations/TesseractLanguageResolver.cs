using Homestead.WebApi.Domains.Settings;

namespace Homestead.WebApi.Features.Documents.TextExtraction.Configurations;

public static class TesseractLanguageResolver
{
    private static readonly string DefaultLanguage = TesseractLanguage.Eng.ToString().ToLowerInvariant();

    public static string Resolve(string? configuration)
    {
        if (!TesseractEngineConfig.TryParse(configuration, out var config))
            return DefaultLanguage;

        if (config!.Languages is { Count: > 0 })
            return string.Join("+", config.Languages.Select(l => l.ToString().ToLowerInvariant()));

        return DefaultLanguage;
    }
}
