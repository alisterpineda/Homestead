using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record AccountSnapshot : IAuditSnapshot<AccountSnapshot, Account>
{
    public required string Name { get; init; }
    public string? Institution { get; init; }

    // Masked, deliberately — the audit log records that the number changed (••••1234 → ••••5678) without
    // ever persisting a raw account number in auditing.audit_logs.
    public string? AccountNumberMasked { get; init; }

    public required bool IsArchived { get; init; }
    public Guid? ParentAccountId { get; init; }

    public static AccountSnapshot From(Account source) => new()
    {
        Name = source.Name,
        Institution = source.Institution,
        AccountNumberMasked = AccountNumberMasking.Mask(source.AccountNumber),
        IsArchived = source.IsArchived,
        ParentAccountId = source.ParentAccountId,
    };

    public List<FieldChange> Diff(AccountSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Name != other.Name)
            changes.Add(new ScalarFieldChange("Name", Name, other.Name));
        if (Institution != other.Institution)
            changes.Add(new ScalarFieldChange("Institution", Institution, other.Institution));
        if (AccountNumberMasked != other.AccountNumberMasked)
            changes.Add(new ScalarFieldChange("AccountNumber", AccountNumberMasked, other.AccountNumberMasked));
        if (IsArchived != other.IsArchived)
            changes.Add(new ScalarFieldChange("IsArchived", IsArchived.ToString(), other.IsArchived.ToString()));
        if (ParentAccountId != other.ParentAccountId)
            changes.Add(new ScalarFieldChange(
                "ParentAccountId", ParentAccountId?.ToString(), other.ParentAccountId?.ToString()));

        return changes;
    }
}
