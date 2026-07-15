using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Finances;

public class Account
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public AccountType AccountType { get; set; }
    public AccountKind? Kind { get; set; }

    // Self-referencing hierarchy — only meaningful for Income/Expense category accounts
    // (IsSystem=false, Kind=null). Balance-sheet accounts leave these null.
    public Guid? ParentAccountId { get; set; }
    public Account? ParentAccount { get; set; }
    public ICollection<Account> ChildAccounts { get; set; } = [];

    public Guid ReportingCurrencyId { get; set; }
    public Commodity ReportingCurrency { get; set; } = null!;

    public string? Institution { get; set; }

    // Full, user-typed account/card/IBAN number. Optional, free-form, no checksum, no encryption
    // (the self-hosted DB is the trust boundary). Masked to last-4 on list/summary reads via
    // AccountNumberMasking; the full value is only returned to the owner on detail/edit.
    public string? AccountNumber { get; set; }

    public bool IsSystem { get; set; }
    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
