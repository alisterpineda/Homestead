using System.Text.Json.Serialization;

namespace Homestead.WebApi.Modules.Documents.Features.Settings;

public class DocumentSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AutoClassifyOnUpload { get; set; }

    // The household's preferred text extraction engine, as a config instance key ("azure-layout").
    // Null means the built-in "tesseract" engine. Validated on write against the configured engines
    // (PatchDocumentSettingsHandler) rather than by type, because what is valid is a deployment fact.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultEngineKey { get; set; }
}
