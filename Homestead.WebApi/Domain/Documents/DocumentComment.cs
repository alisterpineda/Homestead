using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Documents;

public class DocumentComment
{
    public Guid Id { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
}
