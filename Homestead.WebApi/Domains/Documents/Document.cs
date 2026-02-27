using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Domains.Parties;
using Homestead.WebApi.Domains.Tags;

namespace Homestead.WebApi.Domains.Documents;

public class Document
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidUntil { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? OwnerUser { get; set; }

    public ICollection<DocumentVersion> Versions { get; set; } = [];
    public ICollection<DocumentTag> DocumentTags { get; set; } = [];
    public ICollection<DocumentParty> DocumentParties { get; set; } = [];
}
