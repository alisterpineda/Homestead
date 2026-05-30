using System.Text.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Admin.Features.AiProviders.UpdateAiProvider;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin.AiProviders;

public class UpdateAiProviderHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateAiProviderHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
    }

    public void Dispose() => _db.Dispose();

    private UpdateAiProviderHandler Sut() => new(_db, _audit, _unitOfWork);

    private async Task<AiProvider> SeedAsync(string config, AiProviderType type = AiProviderType.OpenAI)
    {
        var provider = new AiProvider
        {
            Id = Guid.NewGuid(),
            Name = "Provider",
            ProviderType = type,
            Configuration = config,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.AiProviders.Add(provider);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return provider;
    }

    private static UpdateAiProviderCommand Command(Guid id, string config, string name = "Provider") =>
        new() { Id = id, Request = new UpdateAiProviderRequest { Name = name, Configuration = config, IsEnabled = true } };

    [Fact]
    public async Task BlankApiKey_ReusesStoredKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = await SeedAsync("""{"apiKey":"super-secret-key-1234","model":"gpt-4o"}""");

        await Sut().Handle(Command(provider.Id, """{"apiKey":"","model":"gpt-4o"}"""), ct);

        Assert.Contains("super-secret-key-1234", _db.AiProviders.Single().Configuration!);
    }

    [Fact]
    public async Task MaskedApiKey_ReusesStoredKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = await SeedAsync("""{"apiKey":"super-secret-key-1234","model":"gpt-4o"}""");
        var masked = SecretMasker.Mask("super-secret-key-1234");

        await Sut().Handle(Command(provider.Id, $$"""{"apiKey":"{{masked}}","model":"gpt-4o"}"""), ct);

        Assert.Contains("super-secret-key-1234", _db.AiProviders.Single().Configuration!);
    }

    [Fact]
    public async Task NewApiKey_Updates()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = await SeedAsync("""{"apiKey":"old-key-value-1234","model":"gpt-4o"}""");

        await Sut().Handle(Command(provider.Id, """{"apiKey":"new-key-value-5678","model":"gpt-4o"}"""), ct);

        var stored = _db.AiProviders.Single().Configuration!;
        Assert.Contains("new-key-value-5678", stored);
        Assert.DoesNotContain("old-key-value-1234", stored);
    }

    [Fact]
    public async Task Response_MasksApiKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = await SeedAsync("""{"apiKey":"old-key-value-1234","model":"gpt-4o"}""");

        var response = await Sut().Handle(
            Command(provider.Id, """{"apiKey":"new-key-value-5678","model":"gpt-4o"}"""), ct);

        Assert.NotNull(response.Configuration);
        Assert.DoesNotContain("new-key-value-5678", response.Configuration);
        var apiKey = JsonDocument.Parse(response.Configuration!).RootElement.GetProperty("apiKey").GetString();
        Assert.StartsWith("••••", apiKey);
        Assert.EndsWith("5678", apiKey);
    }

    [Fact]
    public async Task ValidatesMergedConfig_BlankKeyDoesNotFail()
    {
        // If validation ran on the raw (blank-key) submission it would throw "API Key is required".
        // It passes because validation runs on the merged config (with the stored key restored).
        var ct = TestContext.Current.CancellationToken;
        var provider = await SeedAsync("""{"apiKey":"super-secret-key-1234","model":"gpt-4o"}""");

        var ex = await Record.ExceptionAsync(() =>
            Sut().Handle(Command(provider.Id, """{"apiKey":"","model":"gpt-4o"}"""), ct));

        Assert.Null(ex);
    }
}
