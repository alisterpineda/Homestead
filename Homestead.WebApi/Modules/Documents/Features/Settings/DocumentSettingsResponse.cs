namespace Homestead.WebApi.Modules.Documents.Features.Settings;

public record DocumentSettingsResponse
{
    public Guid? DefaultTextExtractionEngineId { get; init; }
}
