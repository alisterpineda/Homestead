namespace Homestead.WebApi.Features.Admin.v1.Settings;

public record DocumentSettingsResponse
{
    public required int MaxUploadSizeMb { get; init; }
}
