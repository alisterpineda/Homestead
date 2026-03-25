using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Parties;

public class Party
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public AppUser? CreatedByUser { get; set; }
    public ICollection<DocumentParty> DocumentParties { get; set; } = [];
}
