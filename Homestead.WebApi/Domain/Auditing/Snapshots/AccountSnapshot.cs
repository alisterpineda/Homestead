using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record AccountSnapshot : IAuditSnapshot<AccountSnapshot, Account>
{
    public required string Name { get; init; }
    public string? Institution { get; init; }
    public required bool IsArchived { get; init; }
    public Guid? ParentAccountId { get; init; }

    public static AccountSnapshot From(Account source) => new()
    {
        Name = source.Name,
        Institution = source.Institution,
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
        if (IsArchived != other.IsArchived)
            changes.Add(new ScalarFieldChange("IsArchived", IsArchived.ToString(), other.IsArchived.ToString()));
        if (ParentAccountId != other.ParentAccountId)
            changes.Add(new ScalarFieldChange(
                "ParentAccountId", ParentAccountId?.ToString(), other.ParentAccountId?.ToString()));

        return changes;
    }
}
