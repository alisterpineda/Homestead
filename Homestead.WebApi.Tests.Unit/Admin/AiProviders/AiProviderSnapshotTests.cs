using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Tests.Unit.Admin.AiProviders;

public class AiProviderSnapshotTests
{
    // Regression guard: the audit snapshot must record the RAW config (secrets included), unlike
    // CommodityPriceSourceSettingsSnapshot which masks. Audit needs true before/after values.
    [Fact]
    public void From_RecordsRawConfiguration_NotMasked()
    {
        var raw = """{"apiKey":"super-secret-key-1234","model":"gpt-4o"}""";
        var provider = new AiProvider
        {
            Id = Guid.NewGuid(),
            Name = "P",
            ProviderType = AiProviderType.OpenAI,
            Configuration = raw,
            IsEnabled = true,
        };

        var snapshot = AiProviderSnapshot.From(provider);

        Assert.Equal(raw, snapshot.Configuration);
        Assert.Contains("super-secret-key-1234", snapshot.Configuration!);
    }
}
