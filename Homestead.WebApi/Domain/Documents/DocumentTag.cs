namespace Homestead.WebApi.Domain.Documents;

public class DocumentTag
{
    public Guid DocumentId { get; set; }
    public Guid TagId { get; set; }

    public Document Document { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}
