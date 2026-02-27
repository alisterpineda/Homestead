namespace Homestead.WebApi.Domains.Parties;

public class Party
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<DocumentParty> DocumentParties { get; set; } = [];
}
