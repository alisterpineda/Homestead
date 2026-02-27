using Homestead.WebApi.Domains.Documents;

namespace Homestead.WebApi.Domains.Parties;

public class DocumentParty
{
    public Guid DocumentId { get; set; }
    public Guid PartyId { get; set; }

    public Document Document { get; set; } = null!;
    public Party Party { get; set; } = null!;
}
