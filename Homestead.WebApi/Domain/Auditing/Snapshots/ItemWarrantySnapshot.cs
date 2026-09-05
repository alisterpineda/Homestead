using System.Globalization;
using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record ItemWarrantySnapshot : IAuditSnapshot<ItemWarrantySnapshot, ItemWarranty>
{
    public string? Provider { get; init; }
    public DateOnly? ExpiresOn { get; init; }
    public string? Notes { get; init; }

    public static ItemWarrantySnapshot From(ItemWarranty source) => new()
    {
        Provider = source.Provider,
        ExpiresOn = source.ExpiresOn,
        Notes = source.Notes,
    };

    public List<FieldChange> Diff(ItemWarrantySnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Provider != other.Provider)
            changes.Add(new ScalarFieldChange("Provider", Provider, other.Provider));

        if (ExpiresOn != other.ExpiresOn)
            changes.Add(new ScalarFieldChange("ExpiresOn", Format(ExpiresOn), Format(other.ExpiresOn)));

        if (Notes != other.Notes)
            changes.Add(new ScalarFieldChange("Notes", Notes, other.Notes));

        return changes;
    }

    // Round-trippable and culture-independent, matching ItemSnapshot's DateOnly formatting.
    private static string? Format(DateOnly? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture);
}
