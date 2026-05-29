using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Tests.Unit.Admin.AiProviders;

public class AiClientFactoryTests
{
    private readonly AiClientFactory _sut = new();

    private static AiProvider Provider(AiProviderType type, string config) => new()
    {
        Id = Guid.NewGuid(),
        Name = "test",
        ProviderType = type,
        Configuration = config,
        IsEnabled = true,
    };

    [Fact]
    public void CreateClient_OpenAI_WithEndpoint_Builds()
    {
        var provider = Provider(AiProviderType.OpenAI,
            """{"endpoint":"https://api.openai.com/v1","apiKey":"sk-test","model":"gpt-4o"}""");

        var client = _sut.CreateClient(provider);

        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_OpenAI_WithoutEndpoint_Builds()
    {
        var provider = Provider(AiProviderType.OpenAI,
            """{"apiKey":"sk-test","model":"gpt-4o"}""");

        var client = _sut.CreateClient(provider);

        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_OpenAI_MissingApiKey_Throws()
    {
        var provider = Provider(AiProviderType.OpenAI, """{"model":"gpt-4o"}""");

        Assert.Throws<InvalidOperationException>(() => _sut.CreateClient(provider));
    }

    [Fact]
    public void CreateClient_OpenAI_MissingModel_Throws()
    {
        var provider = Provider(AiProviderType.OpenAI, """{"apiKey":"sk-test"}""");

        Assert.Throws<InvalidOperationException>(() => _sut.CreateClient(provider));
    }

    [Fact]
    public void CreateClient_Anthropic_Throws_NotSupported()
    {
        var provider = Provider(AiProviderType.Anthropic, """{"apiKey":"x","model":"claude"}""");

        Assert.Throws<NotSupportedException>(() => _sut.CreateClient(provider));
    }

    [Fact]
    public void CreateClient_Azure_WithFullConfig_Builds()
    {
        var provider = Provider(AiProviderType.AzureAIFoundry,
            """{"endpoint":"https://test.openai.azure.com","apiKey":"key","model":"gpt-4o"}""");

        var client = _sut.CreateClient(provider);

        Assert.NotNull(client);
    }
}
