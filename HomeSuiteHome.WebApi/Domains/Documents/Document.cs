using HomeSuiteHome.WebApi.Domains.Identity;
using HomeSuiteHome.WebApi.Domains.Parties;
using HomeSuiteHome.WebApi.Domains.Tags;

namespace HomeSuiteHome.WebApi.Domains.Documents;

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

    public ICollection<DocumentRevision> Revisions { get; set; } = [];
    public ICollection<DocumentTag> DocumentTags { get; set; } = [];
    public ICollection<DocumentParty> DocumentParties { get; set; } = [];
}
