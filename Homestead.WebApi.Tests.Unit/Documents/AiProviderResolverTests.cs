using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Documents.Features.Settings;
using Homestead.WebApi.Modules.Documents.Services;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class AiProviderResolverTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly AiProviderResolver _sut;

    public AiProviderResolverTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new AiProviderResolver(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ResolveAsync_ExplicitId_ReturnsProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new AiProvider
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            ProviderType = AiProviderType.AzureAIFoundry,
            IsEnabled = true
        };
        _db.AiProviders.Add(provider);
        await _db.SaveChangesAsync(ct);

        var result = await _sut.ResolveAsync(provider.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(provider.Id, result!.Id);
    }

    [Fact]
    public async Task ResolveAsync_ExplicitId_DisabledProvider_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new AiProvider
        {
            Id = Guid.NewGuid(),
            Name = "Disabled",
            ProviderType = AiProviderType.AzureAIFoundry,
            IsEnabled = false
        };
        _db.AiProviders.Add(provider);
        await _db.SaveChangesAsync(ct);

        var result = await _sut.ResolveAsync(provider.Id, ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_NoId_FallsBackToSettings()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new AiProvider
        {
            Id = Guid.NewGuid(),
            Name = "Default",
            ProviderType = AiProviderType.AzureAIFoundry,
            IsEnabled = true
        };
        _db.AiProviders.Add(provider);

        var settings = new DocumentSettings { DefaultAiProviderId = provider.Id };
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.DocumentsModule,
            Value = JsonSerializer.Serialize(settings, FieldChange.JsonOptions)
        });
        await _db.SaveChangesAsync(ct);

        var result = await _sut.ResolveAsync(null, ct);

        Assert.NotNull(result);
        Assert.Equal(provider.Id, result!.Id);
    }

    [Fact]
    public async Task ResolveAsync_NoId_NoSettings_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _sut.ResolveAsync(null, ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_NonExistentId_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _sut.ResolveAsync(Guid.NewGuid(), ct);

        Assert.Null(result);
    }
}
