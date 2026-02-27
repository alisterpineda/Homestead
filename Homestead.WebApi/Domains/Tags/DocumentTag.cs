using Homestead.WebApi.Domains.Documents;

namespace Homestead.WebApi.Domains.Tags;

public class DocumentTag
{
    public Guid DocumentId { get; set; }
    public Guid TagId { get; set; }

    public Document Document { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}
