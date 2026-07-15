using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Modules.Documents.Features.TextExtractionEngines;

/// <summary>
/// One configured engine instance, as offered to the reprocess dialogs and the settings page.
/// </summary>
/// <param name="Key">The instance key to send back as EngineKey ("azure-layout").</param>
/// <param name="Name">Display label for the engine's type — several instances can share one.</param>
/// <param name="IsConfigured">False means selecting it would be rejected; show it disabled.</param>
/// <param name="ConfigurationError">Why it is unavailable, when it is.</param>
public record TextExtractionEngineResponse(
    string Key,
    string Name,
    TextExtractionEngineType Type,
    bool IsConfigured,
    string? ConfigurationError);
