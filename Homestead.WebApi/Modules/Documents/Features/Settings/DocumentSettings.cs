using System.Text.Json.Serialization;

namespace Homestead.WebApi.Modules.Documents.Features.Settings;

public class DocumentSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? DefaultTextExtractionEngineId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? DefaultAiProviderId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AutoClassifyOnUpload { get; set; }
}
