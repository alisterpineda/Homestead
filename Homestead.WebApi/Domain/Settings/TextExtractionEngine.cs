using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Settings;

public class TextExtractionEngine
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public TextExtractionEngineType EngineType { get; set; }
    public string? Configuration { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
}
