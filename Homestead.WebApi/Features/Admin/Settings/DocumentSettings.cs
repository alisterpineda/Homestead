using System.Text.Json.Serialization;

namespace Homestead.WebApi.Features.Admin.Settings;

public class DocumentSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? DefaultTextExtractionEngineId { get; set; }
}
