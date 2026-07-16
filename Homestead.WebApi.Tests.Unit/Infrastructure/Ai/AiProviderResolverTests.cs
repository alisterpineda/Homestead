using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

public class AiProviderResolverTests
{
    private static AiProvider Local(string key = "local") => new(key, new AiProviderOptions
    {
        Type = AiProviderType.OpenAiCompatible,
        Endpoint = "http://homeserver:11434/v1",
        Model = "gemma-4-e4b",
    });

    private static AiProvider Broken(string key = "foundry") => new(key, new AiProviderOptions
    {
        Type = AiProviderType.AzureFoundry,
        Endpoint = "https://res.openai.azure.com",
        // No ApiKey -> defined but unavailable.
        Model = "gpt-5-mini",
    });

    [Fact]
    public void ExplicitKey_ResolvesTheCanonicalInstance_CaseInsensitively()
    {
        var local = Local();
        var resolver = new AiProviderResolver([local, Broken()], defaultProviderKey: null);

        Assert.Same(local, resolver.Resolve("local"));
        Assert.Same(local, resolver.Resolve("LOCAL"));
    }

    // An undefined key resolves to a synthetic unavailable provider carrying the REQUESTED key — never
    // null (which would read as "AI off") and never another provider (which would be silent substitution).
    [Fact]
    public void ExplicitKey_Undefined_ReturnsUnavailableWithReason_NotASubstitute()
    {
        var resolver = new AiProviderResolver([Local()], defaultProviderKey: null);

        var resolved = resolver.Resolve("typo");

        Assert.NotNull(resolved);
        Assert.Equal("typo", resolved.Key);
        Assert.False(resolved.IsConfigured);
        Assert.Equal("AI provider 'typo' is not defined under Ai:Providers.", resolved.ConfigurationError);
        var ex = Assert.Throws<InvalidOperationException>(() => resolved.GetChatClient());
        Assert.Contains("typo", ex.Message);
    }

    // The configured default wins even when it is broken — a misconfigured explicit choice must surface
    // its reason, not quietly run the healthy sibling.
    [Fact]
    public void DefaultKey_Misconfigured_ReturnsThatProvider_NotTheHealthySibling()
    {
        var broken = Broken();
        var resolver = new AiProviderResolver([Local(), broken], defaultProviderKey: "foundry");

        var resolved = resolver.Resolve();

        Assert.Same(broken, resolved);
        Assert.False(resolved.IsConfigured);
        Assert.Equal("ApiKey is required.", resolved.ConfigurationError);
    }

    [Fact]
    public void DanglingDefaultKey_ReturnsUnavailableWithReason_EvenWithOneHealthyProvider()
    {
        var resolver = new AiProviderResolver([Local()], defaultProviderKey: "typo");

        var resolved = resolver.Resolve();

        Assert.NotNull(resolved);
        Assert.Equal("typo", resolved.Key);
        Assert.False(resolved.IsConfigured);
    }

    // Convenience terminus: exactly one defined provider and no default set -> use it (even if it is
    // misconfigured — it then reports its reason rather than pretending AI is off).
    [Fact]
    public void NoKeyNoDefault_SoleProvider_Resolves_EvenWhenMisconfigured()
    {
        var local = Local();
        Assert.Same(local, new AiProviderResolver([local], defaultProviderKey: null).Resolve());

        var broken = Broken();
        Assert.Same(broken, new AiProviderResolver([broken], defaultProviderKey: null).Resolve());
    }

    [Fact]
    public void NoKeyNoDefault_TwoProviders_Unresolved()
    {
        Assert.Null(new AiProviderResolver([Local(), Broken()], defaultProviderKey: null).Resolve());
    }

    [Fact]
    public void NoProviders_Unresolved()
    {
        Assert.Null(new AiProviderResolver([], defaultProviderKey: null).Resolve());
    }

    // Whitespace in the request is "no explicit key", not a key of spaces.
    [Fact]
    public void BlankExplicitKey_FallsThroughToTheDefaultChain()
    {
        var local = Local();
        var resolver = new AiProviderResolver([local], defaultProviderKey: null);

        Assert.Same(local, resolver.Resolve("   "));
    }
}
