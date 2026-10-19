using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Admin.Features.Ai.GetAiStatus;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin.Ai;

// Built over the real AiConfiguration.Validate verdict and real AiInputSettings.From resolution (only the provider
// is a substitute, standing in for the Availability it would compute), so state, error surfacing and the resolved
// knobs are pinned end-to-end rather than against hand-picked values.
public class GetAiStatusHandlerTests
{
    private static AiOptions Configured() => new() { ApiKey = "sk-ant-test", Model = "claude-opus-5-5" };

    private static GetAiStatusHandler Sut(AiOptions options, bool legacyKeysPresent = false)
    {
        var provider = Substitute.For<IAiProvider>();
        provider.Availability.Returns(AiConfiguration.Validate(options, legacyKeysPresent));
        return new GetAiStatusHandler(provider, options, AiInputSettings.From(options));
    }

    [Fact]
    public async Task Configured_ReportsReady_WithModelAndDefaults()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Sut(Configured()).HandleAsync(ct);

        Assert.True(result.IsConfigured);
        Assert.True(result.IsPresent);
        Assert.Null(result.ConfigurationError);
        Assert.Equal("claude-opus-5-5", result.Model);
        Assert.Null(result.Endpoint);   // unset -> the Claude API's default base URL
        Assert.Equal("medium", result.Effort);
        Assert.True(result.RefusalFallback);
        Assert.True(result.ImageInput);
        Assert.Equal(AiOptions.DefaultImageRenderMaxEdge, result.ImageRenderMaxEdge);
        Assert.Equal(AiOptions.DefaultStatementMaxInputChars, result.StatementMaxInputChars);
        Assert.True(result.PdfInput);
        Assert.Equal(AiOptions.DefaultPdfInputMaxPages, result.PdfInputMaxPages);
        Assert.Equal(AiOptions.DefaultPdfInputMaxBytes, result.PdfInputMaxBytes);
    }

    [Fact]
    public async Task NoAiConfig_ReportsNotPresent_WithNoError()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Sut(new AiOptions()).HandleAsync(ct);

        Assert.False(result.IsConfigured);
        Assert.False(result.IsPresent);
        Assert.Null(result.ConfigurationError);
        Assert.Null(result.Model);
        Assert.False(result.HasApiKey);
    }

    [Fact]
    public async Task MisconfiguredAi_SurfacesItsReason()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Sut(new AiOptions { Model = "claude-opus-5-5" }).HandleAsync(ct);

        Assert.False(result.IsConfigured);
        Assert.True(result.IsPresent);
        Assert.Equal("Ai:ApiKey is required.", result.ConfigurationError);
    }

    // The retired multi-provider shape alone is "present but unusable", so the operator who upgraded without
    // renaming sees why AI went quiet instead of a silent off.
    [Fact]
    public async Task LegacyConfigOnly_SurfacesTheLegacyReason()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Sut(new AiOptions(), legacyKeysPresent: true).HandleAsync(ct);

        Assert.False(result.IsConfigured);
        Assert.True(result.IsPresent);
        Assert.Equal(AiConfiguration.LegacyConfigReason, result.ConfigurationError);
    }

    [Fact]
    public async Task ApiKey_NeverEchoed_OnlyPresenceReported()
    {
        var ct = TestContext.Current.CancellationToken;
        const string secret = "sk-ant-super-secret-value";
        var options = Configured();
        options.ApiKey = secret;

        var result = await Sut(options).HandleAsync(ct);

        Assert.True(result.HasApiKey);
        // The raw key must never appear anywhere on the response — ToString() spans every field, so this
        // would catch a regression that added the key value to the response shape.
        Assert.DoesNotContain(secret, result.ToString());
    }

    [Fact]
    public async Task MaxInputChars_IsTheResolvedBudget()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = Configured();
        options.MaxInputChars = AiOptions.MinInputChars - 1; // below floor -> default

        var result = await Sut(options).HandleAsync(ct);

        Assert.Equal(AiOptions.DefaultMaxInputChars, result.MaxInputChars);
    }

    // Effort is reported as the resolved wire value, whatever casing config used; the endpoint override is trimmed.
    [Fact]
    public async Task Effort_IsTheResolvedWireValue_AndEndpointIsTrimmed()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = Configured();
        options.Effort = "XHigh";
        options.Endpoint = "  https://gateway.example/anthropic  ";
        options.RefusalFallback = false;

        var result = await Sut(options).HandleAsync(ct);

        Assert.True(result.IsConfigured);
        Assert.Equal("xhigh", result.Effort);
        Assert.Equal("https://gateway.example/anthropic", result.Endpoint);
        Assert.False(result.RefusalFallback);
    }

    // The PDF knobs are reported as resolved: the switch verbatim, the caps after their floor/ceiling.
    [Fact]
    public async Task PdfInput_ReportsTheResolvedKnobs()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = Configured();
        options.PdfInput = false;
        options.PdfInputMaxPages = 0;                       // below floor -> default
        options.PdfInputMaxBytes = 50L * 1024 * 1024;       // above ceiling -> clamped

        var result = await Sut(options).HandleAsync(ct);

        Assert.False(result.PdfInput);
        Assert.Equal(AiOptions.DefaultPdfInputMaxPages, result.PdfInputMaxPages);
        Assert.Equal(AiOptions.MaxPdfInputMaxBytes, result.PdfInputMaxBytes);
    }
}
