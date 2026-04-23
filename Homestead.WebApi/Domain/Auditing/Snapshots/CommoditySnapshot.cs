using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record CommoditySnapshot : IAuditSnapshot<CommoditySnapshot, Commodity>
{
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required CommodityKind Kind { get; init; }
    public required short DisplayPrecision { get; init; }

    public static CommoditySnapshot From(Commodity source) => new()
    {
        Symbol = source.Symbol,
        Name = source.Name,
        Kind = source.Kind,
        DisplayPrecision = source.DisplayPrecision,
    };

    public List<FieldChange> Diff(CommoditySnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Symbol != other.Symbol)
            changes.Add(new ScalarFieldChange("Symbol", Symbol, other.Symbol));
        if (Name != other.Name)
            changes.Add(new ScalarFieldChange("Name", Name, other.Name));
        if (Kind != other.Kind)
            changes.Add(new ScalarFieldChange("Kind", Kind.ToString(), other.Kind.ToString()));
        if (DisplayPrecision != other.DisplayPrecision)
            changes.Add(new ScalarFieldChange("DisplayPrecision", DisplayPrecision.ToString(), other.DisplayPrecision.ToString()));

        return changes;
    }
}
