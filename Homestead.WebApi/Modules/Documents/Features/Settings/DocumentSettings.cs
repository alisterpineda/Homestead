using System.Text.Json.Serialization;

namespace Homestead.WebApi.Modules.Documents.Features.Settings;

// Product data only. Which text extraction engine to use is deployment config ("Ocr:DefaultEngine"), so
// it is NOT here. A stale "defaultEngineKey" member left in the stored JSON by older releases is skipped
// on read (FieldChange.JsonOptions ignores unknown members) and dropped on the next re-serialisation.
public class DocumentSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AutoClassifyOnUpload { get; set; }
}
