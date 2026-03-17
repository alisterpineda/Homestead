using System.Text.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.TextExtraction.Configurations;

namespace Homestead.WebApi.Features.Admin.v1.OcrEngines;

public static class OcrEngineConfigValidator
{
    public static void Validate(OcrEngineType engineType, string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            return;

        try
        {
            switch (engineType)
            {
                case OcrEngineType.Tesseract:
                    JsonSerializer.Deserialize<TesseractEngineConfig>(configuration);
                    break;
                default:
                    throw new BadRequestException($"Unsupported engine type: {engineType}");
            }
        }
        catch (JsonException)
        {
            throw new BadRequestException("Invalid configuration JSON for the specified engine type.");
        }
    }
}
