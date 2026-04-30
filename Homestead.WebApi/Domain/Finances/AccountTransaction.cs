using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Finances;

public abstract class AccountTransaction
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public required string Memo { get; set; }

    public TransactionType Type { get; set; }

    // v1 usage: DRIP pairing links the Dividend half to its Buy half (and vice versa).
    // OnDelete(SetNull) — deleting one half leaves the survivor as a standalone transaction.
    public Guid? RelatedTransactionId { get; set; }
    public AccountTransaction? RelatedTransaction { get; set; }

    public ICollection<AccountTransactionSplit> Splits { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
