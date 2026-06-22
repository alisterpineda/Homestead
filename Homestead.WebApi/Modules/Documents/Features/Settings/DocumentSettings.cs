using System.Text.Json.Serialization;

namespace Homestead.WebApi.Modules.Documents.Features.Settings;

public class DocumentSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AutoClassifyOnUpload { get; set; }
}
