using System.Text.Json.Serialization;

namespace Homestead.WebApi.Features.Documents.Settings;

public class DocumentSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? DefaultTextExtractionEngineId { get; set; }
}
