using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record PayeeSnapshot : IAuditSnapshot<PayeeSnapshot, Payee>
{
    public required string Name { get; init; }
    public Guid? DefaultCategoryId { get; init; }
    public string? Notes { get; init; }
    public required bool IsArchived { get; init; }

    public static PayeeSnapshot From(Payee source) => new()
    {
        Name = source.Name,
        DefaultCategoryId = source.DefaultCategoryId,
        Notes = source.Notes,
        IsArchived = source.IsArchived,
    };

    public List<FieldChange> Diff(PayeeSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Name != other.Name)
            changes.Add(new ScalarFieldChange("Name", Name, other.Name));
        if (DefaultCategoryId != other.DefaultCategoryId)
            changes.Add(new ScalarFieldChange(
                "DefaultCategoryId", DefaultCategoryId?.ToString(), other.DefaultCategoryId?.ToString()));
        if (Notes != other.Notes)
            changes.Add(new ScalarFieldChange("Notes", Notes, other.Notes));
        if (IsArchived != other.IsArchived)
            changes.Add(new ScalarFieldChange("IsArchived", IsArchived.ToString(), other.IsArchived.ToString()));

        return changes;
    }
}
