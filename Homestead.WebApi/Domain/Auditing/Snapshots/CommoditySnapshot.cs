using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record CommoditySnapshot : IAuditSnapshot<CommoditySnapshot, Commodity>
{
    public required string Symbol { get; init; }
    public required string? Exchange { get; init; }
    public required string Name { get; init; }
    public required CommodityKind Kind { get; init; }
    public required short DisplayPrecision { get; init; }
    public required bool IsSystemManaged { get; init; }

    public static CommoditySnapshot From(Commodity source) => new()
    {
        Symbol = source.Symbol,
        Exchange = source.Exchange,
        Name = source.Name,
        Kind = source.Kind,
        DisplayPrecision = source.DisplayPrecision,
        IsSystemManaged = source.IsSystemManaged,
    };

    public List<FieldChange> Diff(CommoditySnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Symbol != other.Symbol)
            changes.Add(new ScalarFieldChange("Symbol", Symbol, other.Symbol));
        if (Exchange != other.Exchange)
            changes.Add(new ScalarFieldChange("Exchange", Exchange, other.Exchange));
        if (Name != other.Name)
            changes.Add(new ScalarFieldChange("Name", Name, other.Name));
        if (Kind != other.Kind)
            changes.Add(new ScalarFieldChange("Kind", Kind.ToString(), other.Kind.ToString()));
        if (DisplayPrecision != other.DisplayPrecision)
            changes.Add(new ScalarFieldChange("DisplayPrecision", DisplayPrecision.ToString(), other.DisplayPrecision.ToString()));
        if (IsSystemManaged != other.IsSystemManaged)
            changes.Add(new ScalarFieldChange("IsSystemManaged", IsSystemManaged.ToString(), other.IsSystemManaged.ToString()));

        return changes;
    }
}
