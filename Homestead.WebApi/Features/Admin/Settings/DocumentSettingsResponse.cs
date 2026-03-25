namespace Homestead.WebApi.Features.Admin.Settings;

public record DocumentSettingsResponse
{
    public Guid? DefaultTextExtractionEngineId { get; init; }
}
