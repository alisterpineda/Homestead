namespace Homestead.WebApi.Features.Admin.v1.Settings;

public record UpdateDocumentSettingsRequest
{
    public int? MaxUploadSizeMb { get; init; }
}
