using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record TextExtractionEngineSnapshot : IAuditSnapshot<TextExtractionEngineSnapshot, TextExtractionEngine>
{
    public required string Name { get; init; }
    public required string EngineType { get; init; }
    public string? Configuration { get; init; }

    public static TextExtractionEngineSnapshot From(TextExtractionEngine source) => new()
    {
        Name = source.Name,
        EngineType = source.EngineType.ToString(),
        Configuration = source.Configuration
    };

    public List<FieldChange> Diff(TextExtractionEngineSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Name != other.Name)
            changes.Add(new ScalarFieldChange("Name", Name, other.Name));
        if (Configuration != other.Configuration)
            changes.Add(new ScalarFieldChange("Configuration", Configuration, other.Configuration));

        return changes;
    }
}
