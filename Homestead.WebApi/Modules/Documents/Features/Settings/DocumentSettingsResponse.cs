namespace Homestead.WebApi.Modules.Documents.Features.Settings;

public record DocumentSettingsResponse
{
    public Guid? DefaultTextExtractionEngineId { get; init; }
    public Guid? DefaultAiProviderId { get; init; }
    public bool? AutoClassifyOnUpload { get; init; }
}
