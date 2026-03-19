namespace Homestead.WebApi.Features.Admin.v1.Settings;

public record DocumentSettingsResponse
{
    public Guid? DefaultTextExtractionEngineId { get; init; }
}
