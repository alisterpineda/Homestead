using Homestead.WebApi.Configuration;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Features.Admin.v1.Settings.GetDocumentSettings;

public class GetDocumentSettingsHandler(IOptionsMonitor<DocumentSettings> options)
{
    public DocumentSettingsResponse Handle()
    {
        var settings = options.CurrentValue;
        return new DocumentSettingsResponse
        {
            MaxUploadSizeMb = settings.MaxUploadSizeMb,
            DefaultTextExtractionEngineId = settings.DefaultTextExtractionEngineId
        };
    }
}
