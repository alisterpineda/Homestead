using Homestead.WebApi.Domains.Identity;

namespace Homestead.WebApi.Domains.Settings;

public class OcrEngine
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public OcrEngineType EngineType { get; set; }
    public string? Configuration { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
}
