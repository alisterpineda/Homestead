using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Admin.Features.Ai.GetAiStatus;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Tests.Unit.Admin.Ai;

public class GetAiStatusHandlerTests
{
    private static GetAiStatusHandler Sut(AiOptions options) => new(Options.Create(options));

    [Fact]
    public async Task Configured_EchoesEndpointAndModel()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = Sut(new AiOptions
        {
            Endpoint = "http://homeserver:11434/v1",
            Model = "gemma-4-e4b",
        });

        var result = await sut.Handle(new GetAiStatusQuery(), ct);

        Assert.True(result.Configured);
        Assert.Equal("http://homeserver:11434/v1", result.Endpoint);
        Assert.Equal("gemma-4-e4b", result.Model);
    }

    [Fact]
    public async Task BlankEndpoint_IsNotConfigured()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = Sut(new AiOptions { Endpoint = "  ", Model = "gemma-4-e4b" });

        var result = await sut.Handle(new GetAiStatusQuery(), ct);

        Assert.False(result.Configured);
    }

    [Fact]
    public async Task BlankModel_IsNotConfigured()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = Sut(new AiOptions { Endpoint = "http://homeserver:11434/v1", Model = null });

        var result = await sut.Handle(new GetAiStatusQuery(), ct);

        Assert.False(result.Configured);
    }

    [Fact]
    public async Task ApiKeySet_HasApiKeyTrue_KeyNeverEchoed()
    {
        var ct = TestContext.Current.CancellationToken;
        const string secret = "sk-super-secret-value";
        var sut = Sut(new AiOptions
        {
            Endpoint = "http://homeserver:11434/v1",
            Model = "gemma-4-e4b",
            ApiKey = secret,
        });

        var result = await sut.Handle(new GetAiStatusQuery(), ct);

        Assert.True(result.HasApiKey);
        // The raw key must never appear anywhere on the response — ToString() spans every field, so this
        // would catch a regression that added the key value to the response shape.
        Assert.DoesNotContain(secret, result.ToString());
    }

    [Fact]
    public async Task BlankApiKey_HasApiKeyFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = Sut(new AiOptions
        {
            Endpoint = "http://homeserver:11434/v1",
            Model = "gemma-4-e4b",
            ApiKey = "   ",
        });

        var result = await sut.Handle(new GetAiStatusQuery(), ct);

        Assert.False(result.HasApiKey);
    }

    [Fact]
    public async Task MaxInputChars_UnsetReturnsDefault()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = Sut(new AiOptions
        {
            Endpoint = "http://homeserver:11434/v1",
            Model = "gemma-4-e4b",
            MaxInputChars = null,
        });

        var result = await sut.Handle(new GetAiStatusQuery(), ct);

        Assert.Equal(AiOptions.DefaultMaxInputChars, result.MaxInputChars);
    }

    [Fact]
    public async Task MaxInputChars_BelowFloorReturnsDefault()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = Sut(new AiOptions
        {
            Endpoint = "http://homeserver:11434/v1",
            Model = "gemma-4-e4b",
            MaxInputChars = AiOptions.MinInputChars - 1,
        });

        var result = await sut.Handle(new GetAiStatusQuery(), ct);

        Assert.Equal(AiOptions.DefaultMaxInputChars, result.MaxInputChars);
    }

    [Fact]
    public async Task MaxInputChars_AboveFloorReturnsConfiguredValue()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = Sut(new AiOptions
        {
            Endpoint = "http://homeserver:11434/v1",
            Model = "gemma-4-e4b",
            MaxInputChars = 20_000,
        });

        var result = await sut.Handle(new GetAiStatusQuery(), ct);

        Assert.Equal(20_000, result.MaxInputChars);
    }
}
