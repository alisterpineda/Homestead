using System.Text.Json.Serialization;

namespace Homestead.WebApi.Configuration;

public class DocumentSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? DefaultTextExtractionEngineId { get; set; }
}
