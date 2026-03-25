using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

public class TextExtractionEngineResolverTests : IDisposable
{
    private readonly TestAppDbContext _db;

    public TextExtractionEngineResolverTests()
    {
        _db = TestAppDbContext.Create();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private TextExtractionEngineResolver CreateResolver()
        => new(_db);

    private async Task SetDefaultEngineId(Guid? engineId)
    {
        var settings = new { defaultTextExtractionEngineId = engineId };
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.DocumentsModule,
            Value = JsonSerializer.Serialize(settings, FieldChange.JsonOptions)
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task ResolveAsync_NullEngineId_NullDefault_ReturnsBuiltInDefault()
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(null, CancellationToken.None);

        Assert.Equal(TextExtractionEngineType.Tesseract, result.EngineType);
        Assert.Equal(BuiltInTextExtractionEngines.TesseractDefaultConfiguration, result.Configuration);
    }

    [Fact]
    public async Task ResolveAsync_NullEngineId_DefaultIsBuiltInId_ReturnsBuiltInDefault()
    {
        await SetDefaultEngineId(BuiltInTextExtractionEngines.TesseractDefaultId);
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(null, CancellationToken.None);

        Assert.Equal(TextExtractionEngineType.Tesseract, result.EngineType);
        Assert.Equal(BuiltInTextExtractionEngines.TesseractDefaultConfiguration, result.Configuration);
    }

    [Fact]
    public async Task ResolveAsync_NullEngineId_DefaultIsDbEngine_ReturnsDbEngineConfig()
    {
        var engineId = Guid.NewGuid();
        var config = """{"languages":["Fra","Deu"]}""";
        _db.TextExtractionEngines.Add(new TextExtractionEngine
        {
            Id = engineId,
            Name = "French+German",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = config,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        await SetDefaultEngineId(engineId);
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(null, CancellationToken.None);

        Assert.Equal(TextExtractionEngineType.Tesseract, result.EngineType);
        Assert.Equal(config, result.Configuration);
    }

    [Fact]
    public async Task ResolveAsync_ExplicitBuiltInId_ReturnsBuiltInDefault()
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(BuiltInTextExtractionEngines.TesseractDefaultId, CancellationToken.None);

        Assert.Equal(TextExtractionEngineType.Tesseract, result.EngineType);
        Assert.Equal(BuiltInTextExtractionEngines.TesseractDefaultConfiguration, result.Configuration);
    }

    [Fact]
    public async Task ResolveAsync_ExplicitDbEngineId_ReturnsDbEngineConfig()
    {
        var engineId = Guid.NewGuid();
        var config = """{"languages":["Jpn"]}""";
        _db.TextExtractionEngines.Add(new TextExtractionEngine
        {
            Id = engineId,
            Name = "Japanese",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = config,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(engineId, CancellationToken.None);

        Assert.Equal(TextExtractionEngineType.Tesseract, result.EngineType);
        Assert.Equal(config, result.Configuration);
    }

    [Fact]
    public async Task ResolveAsync_ExplicitNonExistentId_FallsBackToBuiltInDefault()
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(TextExtractionEngineType.Tesseract, result.EngineType);
        Assert.Equal(BuiltInTextExtractionEngines.TesseractDefaultConfiguration, result.Configuration);
    }

    [Fact]
    public async Task ResolveAsync_DefaultSetToDeletedEngine_FallsBackToBuiltInDefault()
    {
        await SetDefaultEngineId(Guid.NewGuid()); // non-existent engine
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(null, CancellationToken.None);

        Assert.Equal(TextExtractionEngineType.Tesseract, result.EngineType);
        Assert.Equal(BuiltInTextExtractionEngines.TesseractDefaultConfiguration, result.Configuration);
    }
}
