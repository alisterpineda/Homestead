using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Finances;

public class Account
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public AccountType AccountType { get; set; }

    public Guid? ParentId { get; set; }
    public Account? Parent { get; set; }
    public ICollection<Account> Children { get; set; } = [];

    public Guid DefaultCommodityId { get; set; }
    public Commodity DefaultCommodity { get; set; } = null!;

    public bool IsMultiPosition { get; set; }
    public string? Institution { get; set; }
    public bool IsSystem { get; set; }
    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
