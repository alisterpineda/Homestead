using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Admin.Features.AiProviders;

namespace Homestead.WebApi.Tests.Unit.Admin.AiProviders;

public class AiProviderConfigValidatorTests
{
    [Fact]
    public void OpenAI_KeyAndModel_NoEndpoint_Valid()
    {
        AiProviderConfigValidator.Validate(AiProviderType.OpenAI,
            """{"apiKey":"sk-test","model":"gpt-4o"}""");
    }

    [Fact]
    public void OpenAI_WithValidEndpoint_Valid()
    {
        AiProviderConfigValidator.Validate(AiProviderType.OpenAI,
            """{"apiKey":"sk-test","model":"gpt-4o","endpoint":"https://api.openai.com/v1"}""");
    }

    [Fact]
    public void OpenAI_MissingApiKey_Throws()
    {
        Assert.Throws<BusinessRuleException>(() =>
            AiProviderConfigValidator.Validate(AiProviderType.OpenAI, """{"model":"gpt-4o"}"""));
    }

    [Fact]
    public void OpenAI_MissingModel_Throws()
    {
        Assert.Throws<BusinessRuleException>(() =>
            AiProviderConfigValidator.Validate(AiProviderType.OpenAI, """{"apiKey":"sk-test"}"""));
    }

    [Fact]
    public void OpenAI_InvalidEndpoint_Throws()
    {
        Assert.Throws<BusinessRuleException>(() =>
            AiProviderConfigValidator.Validate(AiProviderType.OpenAI,
                """{"apiKey":"sk-test","model":"gpt-4o","endpoint":"not-a-uri"}"""));
    }

    [Fact]
    public void Azure_MissingEndpoint_Throws()
    {
        Assert.Throws<BusinessRuleException>(() =>
            AiProviderConfigValidator.Validate(AiProviderType.AzureAIFoundry,
                """{"apiKey":"k","model":"m"}"""));
    }

    [Fact]
    public void Azure_FullConfig_Valid()
    {
        AiProviderConfigValidator.Validate(AiProviderType.AzureAIFoundry,
            """{"endpoint":"https://x.openai.azure.com","apiKey":"k","model":"gpt-4o"}""");
    }

    [Fact]
    public void Anthropic_Throws()
    {
        Assert.Throws<BusinessRuleException>(() =>
            AiProviderConfigValidator.Validate(AiProviderType.Anthropic,
                """{"apiKey":"k","model":"m"}"""));
    }

    [Fact]
    public void MaxInputChars_AboveFloor_Valid()
    {
        AiProviderConfigValidator.Validate(AiProviderType.OpenAI,
            """{"apiKey":"sk-test","model":"gpt-4o","maxInputChars":40000}""");
    }

    [Fact]
    public void MaxInputChars_BelowFloor_Throws()
    {
        // A budget the classifier would silently ignore is rejected on write rather than misleading the admin.
        Assert.Throws<BusinessRuleException>(() =>
            AiProviderConfigValidator.Validate(AiProviderType.OpenAI,
                """{"apiKey":"sk-test","model":"gpt-4o","maxInputChars":10}"""));
    }

    [Fact]
    public void MaxInputChars_Omitted_Valid()
    {
        // The common case — no override — stays valid (null = use the default budget).
        AiProviderConfigValidator.Validate(AiProviderType.OpenAI,
            """{"apiKey":"sk-test","model":"gpt-4o"}""");
    }
}
