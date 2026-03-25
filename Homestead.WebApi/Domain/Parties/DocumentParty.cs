using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Domain.Parties;

public class DocumentParty
{
    public Guid DocumentId { get; set; }
    public Guid PartyId { get; set; }

    public Document Document { get; set; } = null!;
    public Party Party { get; set; } = null!;
}
