using System.Globalization;
using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record ItemSnapshot : IAuditSnapshot<ItemSnapshot, Item>
{
    // Fungibility is immutable for the life of the row, so it is not tracked here.
    public required string Name { get; init; }
    public required ItemStatus Status { get; init; }
    public string? Notes { get; init; }
    public Guid? AreaId { get; init; }
    public Guid? ParentItemId { get; init; }
    public required bool IsContainer { get; init; }
    public string? Compartment { get; init; }
    public decimal? Value { get; init; }
    public Guid? ValueCurrencyId { get; init; }
    public string? Serial { get; init; }
    public DateOnly? AcquiredOn { get; init; }
    public string? LentTo { get; init; }
    public string? Unit { get; init; }
    public decimal? RestockThreshold { get; init; }

    public static ItemSnapshot From(Item source) => new()
    {
        Name = source.Name,
        Status = source.Status,
        Notes = source.Notes,
        AreaId = source.AreaId,
        ParentItemId = source.ParentItemId,
        IsContainer = source.IsContainer,
        Compartment = source.Compartment,
        Value = source.Value,
        ValueCurrencyId = source.ValueCurrencyId,
        Serial = source.Serial,
        AcquiredOn = source.AcquiredOn,
        LentTo = source.LentTo,
        Unit = source.Unit,
        RestockThreshold = source.RestockThreshold,
    };

    public List<FieldChange> Diff(ItemSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Name != other.Name)
            changes.Add(new ScalarFieldChange("Name", Name, other.Name));
        if (Status != other.Status)
            changes.Add(new ScalarFieldChange("Status", Status.ToString(), other.Status.ToString()));
        if (Notes != other.Notes)
            changes.Add(new ScalarFieldChange("Notes", Notes, other.Notes));
        if (AreaId != other.AreaId)
            changes.Add(new ScalarFieldChange("AreaId", AreaId?.ToString(), other.AreaId?.ToString()));
        if (ParentItemId != other.ParentItemId)
            changes.Add(new ScalarFieldChange("ParentItemId", ParentItemId?.ToString(), other.ParentItemId?.ToString()));
        if (IsContainer != other.IsContainer)
            changes.Add(new ScalarFieldChange("IsContainer", IsContainer.ToString(), other.IsContainer.ToString()));
        if (Compartment != other.Compartment)
            changes.Add(new ScalarFieldChange("Compartment", Compartment, other.Compartment));
        if (Value != other.Value)
            changes.Add(new ScalarFieldChange("Value", Format(Value), Format(other.Value)));
        if (ValueCurrencyId != other.ValueCurrencyId)
            changes.Add(new ScalarFieldChange("ValueCurrencyId", ValueCurrencyId?.ToString(), other.ValueCurrencyId?.ToString()));
        if (Serial != other.Serial)
            changes.Add(new ScalarFieldChange("Serial", Serial, other.Serial));
        if (AcquiredOn != other.AcquiredOn)
            changes.Add(new ScalarFieldChange("AcquiredOn", Format(AcquiredOn), Format(other.AcquiredOn)));
        if (LentTo != other.LentTo)
            changes.Add(new ScalarFieldChange("LentTo", LentTo, other.LentTo));
        if (Unit != other.Unit)
            changes.Add(new ScalarFieldChange("Unit", Unit, other.Unit));
        if (RestockThreshold != other.RestockThreshold)
            changes.Add(new ScalarFieldChange("RestockThreshold", Format(RestockThreshold), Format(other.RestockThreshold)));

        return changes;
    }

    private static string? Format(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static string? Format(DateOnly? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture);
}
