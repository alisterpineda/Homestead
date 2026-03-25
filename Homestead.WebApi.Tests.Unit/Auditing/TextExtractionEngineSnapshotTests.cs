using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Tests.Unit.Auditing;

public class TextExtractionEngineSnapshotTests
{
    [Fact]
    public void From_CapturesAllFields()
    {
        var engine = new TextExtractionEngine
        {
            Id = Guid.NewGuid(),
            Name = "Custom Engine",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = """{"languages":["Fra"]}""",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var snapshot = TextExtractionEngineSnapshot.From(engine);

        Assert.Equal("Custom Engine", snapshot.Name);
        Assert.Equal("Tesseract", snapshot.EngineType);
        Assert.Equal("""{"languages":["Fra"]}""", snapshot.Configuration);
    }

    [Fact]
    public void Diff_NoChanges_ReturnsEmpty()
    {
        var engine = new TextExtractionEngine
        {
            Id = Guid.NewGuid(),
            Name = "Engine",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = """{"languages":["Eng"]}""",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var before = TextExtractionEngineSnapshot.From(engine);
        var after = TextExtractionEngineSnapshot.From(engine);

        var changes = before.Diff(after);

        Assert.Empty(changes);
    }

    [Fact]
    public void Diff_NameChanged_ReturnsScalarChange()
    {
        var engine = new TextExtractionEngine
        {
            Id = Guid.NewGuid(),
            Name = "Old Name",
            EngineType = TextExtractionEngineType.Tesseract,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var before = TextExtractionEngineSnapshot.From(engine);
        engine.Name = "New Name";
        var after = TextExtractionEngineSnapshot.From(engine);

        var changes = before.Diff(after);

        Assert.Single(changes);
        var change = Assert.IsType<ScalarFieldChange>(changes[0]);
        Assert.Equal("Name", change.Field);
        Assert.Equal("Old Name", change.OldValue);
        Assert.Equal("New Name", change.NewValue);
    }

    [Fact]
    public void Diff_ConfigurationChanged_ReturnsScalarChange()
    {
        var engine = new TextExtractionEngine
        {
            Id = Guid.NewGuid(),
            Name = "Engine",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = """{"languages":["Eng"]}""",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var before = TextExtractionEngineSnapshot.From(engine);
        engine.Configuration = """{"languages":["Fra","Deu"]}""";
        var after = TextExtractionEngineSnapshot.From(engine);

        var changes = before.Diff(after);

        Assert.Single(changes);
        var change = Assert.IsType<ScalarFieldChange>(changes[0]);
        Assert.Equal("Configuration", change.Field);
    }

    [Fact]
    public void Diff_BothChanged_ReturnsTwoChanges()
    {
        var engine = new TextExtractionEngine
        {
            Id = Guid.NewGuid(),
            Name = "Old",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = """{"languages":["Eng"]}""",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var before = TextExtractionEngineSnapshot.From(engine);
        engine.Name = "New";
        engine.Configuration = """{"languages":["Fra"]}""";
        var after = TextExtractionEngineSnapshot.From(engine);

        var changes = before.Diff(after);

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.Field == "Name");
        Assert.Contains(changes, c => c.Field == "Configuration");
    }

    [Fact]
    public void Diff_ConfigurationFromNullToValue_ReturnsChange()
    {
        var engine = new TextExtractionEngine
        {
            Id = Guid.NewGuid(),
            Name = "Engine",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = null,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var before = TextExtractionEngineSnapshot.From(engine);
        engine.Configuration = """{"languages":["Eng"]}""";
        var after = TextExtractionEngineSnapshot.From(engine);

        var changes = before.Diff(after);

        Assert.Single(changes);
        var change = Assert.IsType<ScalarFieldChange>(changes[0]);
        Assert.Equal("Configuration", change.Field);
        Assert.Null(change.OldValue);
        Assert.NotNull(change.NewValue);
    }

    [Fact]
    public void Diff_EngineTypeNotTracked()
    {
        // EngineType is intentionally not tracked in diffs (read-only after creation)
        var snapshot1 = new TextExtractionEngineSnapshot
        {
            Name = "Engine",
            EngineType = "Tesseract",
            Configuration = null
        };
        var snapshot2 = new TextExtractionEngineSnapshot
        {
            Name = "Engine",
            EngineType = "SomethingElse",
            Configuration = null
        };

        var changes = snapshot1.Diff(snapshot2);

        Assert.Empty(changes);
    }
}
