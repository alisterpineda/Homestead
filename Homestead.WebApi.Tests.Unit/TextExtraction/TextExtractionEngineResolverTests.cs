using Homestead.WebApi.Configuration;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.TextExtraction;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.TextExtraction;

public class TextExtractionEngineResolverTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IOptionsMonitor<DocumentSettings> _options;
    private readonly DocumentSettings _settings;

    public TextExtractionEngineResolverTests()
    {
        _db = TestAppDbContext.Create();
        _settings = new DocumentSettings();
        _options = Substitute.For<IOptionsMonitor<DocumentSettings>>();
        _options.CurrentValue.Returns(_settings);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private TextExtractionEngineResolver CreateResolver()
        => new(_db, _options);

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
        _settings.DefaultTextExtractionEngineId = BuiltInTextExtractionEngines.TesseractDefaultId;
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

        _settings.DefaultTextExtractionEngineId = engineId;
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
        _settings.DefaultTextExtractionEngineId = Guid.NewGuid(); // non-existent
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(null, CancellationToken.None);

        Assert.Equal(TextExtractionEngineType.Tesseract, result.EngineType);
        Assert.Equal(BuiltInTextExtractionEngines.TesseractDefaultConfiguration, result.Configuration);
    }
}
