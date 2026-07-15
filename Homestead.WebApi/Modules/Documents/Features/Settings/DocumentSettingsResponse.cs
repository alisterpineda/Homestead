namespace Homestead.WebApi.Modules.Documents.Features.Settings;

public record DocumentSettingsResponse
{
    public bool? AutoClassifyOnUpload { get; init; }
    public string? DefaultEngineKey { get; init; }
}
