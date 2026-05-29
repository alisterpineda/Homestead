using System.Text.Json;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Admin.Features.AiProviders.GetAiProviders;

namespace Homestead.WebApi.Tests.Unit.Admin.AiProviders;

public class GetAiProvidersHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;

    public GetAiProvidersHandlerTests() => _db = TestAppDbContext.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_MasksApiKeyInConfiguration()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.AiProviders.Add(new AiProvider
        {
            Id = Guid.NewGuid(),
            Name = "P",
            ProviderType = AiProviderType.OpenAI,
            Configuration = """{"apiKey":"super-secret-key-1234","model":"gpt-4o"}""",
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        var result = await new GetAiProvidersHandler(_db).Handle(
            new GetAiProvidersQuery { Page = 1, PageSize = 10 }, ct);

        var item = Assert.Single(result.Items);
        Assert.NotNull(item.Configuration);
        // The raw secret must never appear; the masked sentinel is JSON-escaped on the wire
        // (•) so parse the value to verify the decoded form.
        Assert.DoesNotContain("super-secret-key-1234", item.Configuration);
        var root = JsonDocument.Parse(item.Configuration!).RootElement;
        var apiKey = root.GetProperty("apiKey").GetString();
        Assert.StartsWith("••••", apiKey);
        Assert.EndsWith("1234", apiKey);
        // Non-secret keys remain visible.
        Assert.Equal("gpt-4o", root.GetProperty("model").GetString());
    }
}
