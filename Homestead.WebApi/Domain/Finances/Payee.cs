using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Finances;

public class Payee
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Guid? DefaultCategoryId { get; set; }
    public Account? DefaultCategory { get; set; }
    public string? Notes { get; set; }
    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
