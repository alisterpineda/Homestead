using Homestead.WebApi.Domains.Settings;

namespace Homestead.WebApi.TextExtraction.Configurations;

public static class TesseractLanguageResolver
{
    private static readonly string DefaultLanguage = TesseractLanguage.Eng.ToString().ToLowerInvariant();

    public static string Resolve(TextExtractionContext context)
    {
        if (!TesseractEngineConfig.TryParse(context.Configuration, out var config))
            return DefaultLanguage;

        if (config!.Languages is { Count: > 0 })
            return string.Join("+", config.Languages.Select(l => l.ToString().ToLowerInvariant()));

        return DefaultLanguage;
    }
}
