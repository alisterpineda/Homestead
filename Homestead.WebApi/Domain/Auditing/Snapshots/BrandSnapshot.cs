using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record BrandSnapshot : IAuditSnapshot<BrandSnapshot, Brand>
{
    public required string Name { get; init; }

    public static BrandSnapshot From(Brand source) => new()
    {
        Name = source.Name,
    };

    public List<FieldChange> Diff(BrandSnapshot other) =>
        Name != other.Name ? [new ScalarFieldChange("Name", Name, other.Name)] : [];
}
