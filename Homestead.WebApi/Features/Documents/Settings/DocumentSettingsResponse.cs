namespace Homestead.WebApi.Features.Documents.Settings;

public record DocumentSettingsResponse
{
    public Guid? DefaultTextExtractionEngineId { get; init; }
}
