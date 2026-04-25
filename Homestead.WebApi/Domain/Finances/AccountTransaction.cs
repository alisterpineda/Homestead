using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Finances;

public abstract class AccountTransaction
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public required string Description { get; set; }

    public TransactionType Type { get; set; }

    public ICollection<AccountTransactionSplit> Splits { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
