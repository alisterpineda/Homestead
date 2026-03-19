using System.Text.Json.Serialization;

namespace Homestead.WebApi.Configuration;

public class DocumentSettings
{
    public int MaxUploadSizeMb { get; set; } = 100;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? DefaultTextExtractionEngineId { get; set; }
}
