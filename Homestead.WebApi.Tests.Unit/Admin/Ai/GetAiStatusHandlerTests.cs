using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Admin.Features.Ai.GetAiStatus;

namespace Homestead.WebApi.Tests.Unit.Admin.Ai;

// Built over real AiProvider instances + the real AiProviderResolver (both directly newable) so the list
// shape, default marking, and error surfacing are pinned end-to-end rather than against substitutes.
public class GetAiStatusHandlerTests
{
    private static AiProvider Provider(string key, AiProviderOptions? options = null) =>
        new(key, options ?? new AiProviderOptions
        {
            Type = AiProviderType.OpenAiCompatible,
            Endpoint = "http://homeserver:11434/v1",
            Model = "gemma-4-e4b",
        });

    private static GetAiStatusHandler Sut(List<AiProvider> providers, string? defaultKey = null) =>
        new(providers, new AiProviderResolver(providers, defaultKey));

    [Fact]
    public async Task Providers_ListedOrderedByKey_WithDefaultMarked()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = Sut([Provider("local"), Provider("foundry", new AiProviderOptions
        {
            Type = AiProviderType.AzureFoundry,
            Endpoint = "https://res.openai.azure.com",
            ApiKey = "k",
            Model = "gpt-5-mini",
        })], defaultKey: "foundry");

        var result = await sut.HandleAsync(ct);

        Assert.Equal(["foundry", "local"], result.Select(r => r.Key));
        Assert.Collection(result,
            r =>
            {
                Assert.True(r.IsDefault);
                Assert.True(r.IsConfigured);
                Assert.Equal(AiProviderType.AzureFoundry, r.Type);
                Assert.Equal("gpt-5-mini", r.Model);
            },
            r => Assert.False(r.IsDefault));
    }

    [Fact]
    public async Task SoleProvider_IsMarkedDefault_WithoutAnyDefaultKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = Sut([Provider("local")]);

        var result = await sut.HandleAsync(ct);

        var row = Assert.Single(result);
        Assert.True(row.IsDefault);
    }

    [Fact]
    public async Task UnavailableProvider_SurfacesItsReason()
    {
        var ct = TestContext.Current.CancellationToken;
        // Type present but nothing else -> "Endpoint is required."; also a provider with no Type at all.
        var sut = Sut([
            Provider("half", new AiProviderOptions { Type = AiProviderType.AzureFoundry }),
            Provider("typeless", new AiProviderOptions { Endpoint = "http://x/v1", Model = "m" }),
        ]);

        var result = await sut.HandleAsync(ct);

        Assert.Equal("Endpoint is required.", result.Single(r => r.Key == "half").ConfigurationError);
        var typeless = result.Single(r => r.Key == "typeless");
        Assert.Null(typeless.Type);
        Assert.False(typeless.IsConfigured);
        Assert.StartsWith("Type is required", typeless.ConfigurationError);
    }

    [Fact]
    public async Task ApiKey_NeverEchoed_OnlyPresenceReported()
    {
        var ct = TestContext.Current.CancellationToken;
        const string secret = "sk-super-secret-value";
        var sut = Sut([Provider("foundry", new AiProviderOptions
        {
            Type = AiProviderType.AzureFoundry,
            Endpoint = "https://res.openai.azure.com",
            ApiKey = secret,
            Model = "gpt-5-mini",
        })]);

        var result = await sut.HandleAsync(ct);

        var row = Assert.Single(result);
        Assert.True(row.HasApiKey);
        // The raw key must never appear anywhere on the response — ToString() spans every field, so this
        // would catch a regression that added the key value to the response shape.
        Assert.DoesNotContain(secret, row.ToString());
    }

    [Fact]
    public async Task MaxInputChars_IsTheResolvedBudget()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new AiProviderOptions
        {
            Type = AiProviderType.OpenAiCompatible,
            Endpoint = "http://homeserver:11434/v1",
            Model = "gemma-4-e4b",
            MaxInputChars = AiProviderOptions.MinInputChars - 1, // below floor -> default
        };

        var result = await Sut([Provider("local", options)]).HandleAsync(ct);

        Assert.Equal(AiProviderOptions.DefaultMaxInputChars, Assert.Single(result).MaxInputChars);
    }

    // The contract the frontend's "no default selected" warning relies on: multiple providers with no
    // default key produce a list where NOTHING is marked default (and no synthetic row is appended).
    [Fact]
    public async Task MultipleProviders_NoDefaultKey_MarksNoneDefault()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = Sut([Provider("local"), Provider("other")]);

        var result = await sut.HandleAsync(ct);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, r => r.IsDefault);
    }

    [Fact]
    public async Task NoProviders_EmptyList()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Sut([]).HandleAsync(ct);

        Assert.Empty(result);
    }

    // A dangling Ai:DefaultProvider still shows up — as an unavailable row marked default — because the
    // admin page is where an operator debugs exactly this typo.
    [Fact]
    public async Task DanglingDefault_AppendedAsUnavailableDefaultRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = Sut([Provider("local")], defaultKey: "typo");

        var result = await sut.HandleAsync(ct);

        Assert.Equal(2, result.Count);
        Assert.False(result.Single(r => r.Key == "local").IsDefault);
        var dangling = result.Single(r => r.Key == "typo");
        Assert.True(dangling.IsDefault);
        Assert.False(dangling.IsConfigured);
        Assert.Equal("AI provider 'typo' is not defined under Ai:Providers.", dangling.ConfigurationError);
    }
}
