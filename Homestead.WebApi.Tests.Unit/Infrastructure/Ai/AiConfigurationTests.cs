using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

// The "Ai" section -> AiAvailability (absent / unavailable-with-reason / ready), legacy-key detection, effort
// parsing, and the input-knob floors resolved into AiInputSettings.
public class AiConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static AiOptions Bind(IConfiguration config) =>
        config.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();

    private static AiAvailability Availability(params (string Key, string? Value)[] values)
    {
        var config = Config(values);
        return AiConfiguration.Validate(Bind(config), AiConfiguration.LegacyKeysPresent(config));
    }

    [Fact]
    public void NoAiSection_IsAbsent() =>
        Assert.Equal(AiAvailability.Absent, Availability());

    // The input knobs have defaults and mean nothing without a model, so setting one alone isn't "configuring AI".
    [Fact]
    public void KnobsAlone_AreStillAbsent() =>
        Assert.Equal(AiAvailability.Absent, Availability(("Ai:ImageInput", "false"), ("Ai:MaxInputChars", "5000")));

    [Fact]
    public void KeyAndModel_AreReady() =>
        Assert.Equal(AiAvailability.Ready, Availability(("Ai:ApiKey", "sk"), ("Ai:Model", "claude-opus-5-5")));

    [Theory]
    [InlineData("Ai:Model", "claude-opus-5-5", "Ai:ApiKey is required.")]
    [InlineData("Ai:ApiKey", "sk", "Ai:Model is required.")]
    public void MissingKeyOrModel_IsUnavailableWithReason(string key, string value, string reason) =>
        Assert.Equal(AiAvailability.Unavailable(reason), Availability((key, value)));

    [Fact]
    public void RelativeEndpoint_IsUnavailableWithReason() =>
        Assert.Equal(AiAvailability.Unavailable("Ai:Endpoint must be an absolute URI."),
            Availability(("Ai:ApiKey", "sk"), ("Ai:Model", "m"), ("Ai:Endpoint", "api.example.com")));

    [Fact]
    public void AbsoluteEndpoint_IsReady() =>
        Assert.Equal(AiAvailability.Ready,
            Availability(("Ai:ApiKey", "sk"), ("Ai:Model", "m"), ("Ai:Endpoint", "https://proxy.example.com")));

    [Theory]
    [InlineData(null, AiEffort.Medium)]
    [InlineData("", AiEffort.Medium)]
    [InlineData("low", AiEffort.Low)]
    [InlineData("MEDIUM", AiEffort.Medium)]
    [InlineData(" High ", AiEffort.High)]
    [InlineData("xhigh", AiEffort.XHigh)]
    [InlineData("max", AiEffort.Max)]
    public void Effort_ParsesCaseInsensitively(string? value, AiEffort expected) =>
        Assert.Equal(expected, AiConfiguration.ParseEffort(value));

    [Fact]
    public void UnknownEffort_IsUnavailableWithReason()
    {
        var availability = Availability(("Ai:ApiKey", "sk"), ("Ai:Model", "m"), ("Ai:Effort", "extreme"));

        Assert.False(availability.IsConfigured);
        Assert.True(availability.IsPresent);
        Assert.Equal("Ai:Effort 'extreme' is not one of: low, medium, high, xhigh, max.", availability.Reason);
    }

    [Fact]
    public void RefusalFallback_DefaultsOn_AndBindsOff()
    {
        Assert.True(Bind(Config()).RefusalFallback);
        Assert.False(Bind(Config(("Ai:RefusalFallback", "false"))).RefusalFallback);
    }

    // The silent-no-op hazard: an operator who upgrades without renaming must be told, not quietly lose AI.
    [Theory]
    [InlineData("Ai:Providers:local:Type", "Anthropic")]
    [InlineData("Ai:DefaultProvider", "local")]
    public void LegacyKeysOnly_AreUnavailableWithTheLegacyReason(string key, string value) =>
        Assert.Equal(AiAvailability.Unavailable(AiConfiguration.LegacyConfigReason), Availability((key, value)));

    // Both shapes may coexist during the transition (the rollback story): the new keys win.
    [Fact]
    public void LegacyKeysAlongsideNewKeys_AreReady() =>
        Assert.Equal(AiAvailability.Ready, Availability(
            ("Ai:Providers:local:Type", "Anthropic"), ("Ai:DefaultProvider", "local"),
            ("Ai:ApiKey", "sk"), ("Ai:Model", "claude-opus-5-5")));

    [Fact]
    public void InputSettings_DefaultWhenUnset()
    {
        var settings = AiInputSettings.From(new AiOptions());

        Assert.Equal(new AiInputSettings(AiEffort.Medium, AiOptions.DefaultMaxInputChars,
            AiOptions.DefaultStatementMaxInputChars, true, AiOptions.DefaultImageRenderMaxEdge,
            true, AiOptions.DefaultPdfInputMaxPages, AiOptions.DefaultUploadPdfInputMaxBytes), settings);
    }

    // A page cap below 1 and a byte cap below the 64 KiB floor fall back to their defaults, like the other knobs; a
    // byte cap above 20 MiB and a page cap above 100 clamp down to those ceilings.
    [Theory]
    [InlineData(0, 1_000, AiOptions.DefaultPdfInputMaxPages, AiOptions.DefaultUploadPdfInputMaxBytes)]
    [InlineData(-5, 50L * 1024 * 1024, AiOptions.DefaultPdfInputMaxPages, AiOptions.MaxPdfInputMaxBytes)]
    [InlineData(1, AiOptions.MinPdfInputMaxBytes, 1, AiOptions.MinPdfInputMaxBytes)]
    [InlineData(100, 5L * 1024 * 1024, 100, 5L * 1024 * 1024)]
    [InlineData(500, 5L * 1024 * 1024, AiOptions.MaxPdfInputMaxPages, 5L * 1024 * 1024)]
    public void InputSettings_PdfCaps_ApplyFloorsAndCeiling(
        int maxPages, long maxBytes, int expectedPages, long expectedBytes)
    {
        var settings = AiInputSettings.From(new AiOptions
        {
            PdfInput = false, PdfInputMaxPages = maxPages, PdfInputMaxBytes = maxBytes,
        });

        Assert.False(settings.PdfInput);
        Assert.Equal(expectedPages, settings.PdfInputMaxPages);
        Assert.Equal(expectedBytes, settings.PdfInputMaxBytes);
    }

    // Unset, the byte cap follows PdfUpload: 20 MiB when uploading, 4 MiB inline (where a PDF request holds ~20x
    // the file in memory). A configured value wins either way, clamped to the ceiling (checked inline, where the
    // default differs from the ceiling, so a clamp can't pass as a fall-back to the default).
    [Theory]
    [InlineData(true, null, AiOptions.DefaultUploadPdfInputMaxBytes)]
    [InlineData(false, null, AiOptions.DefaultInlinePdfInputMaxBytes)]
    [InlineData(false, 1_000L, AiOptions.DefaultInlinePdfInputMaxBytes)]
    [InlineData(false, 8L * 1024 * 1024, 8L * 1024 * 1024)]
    [InlineData(true, 2L * 1024 * 1024, 2L * 1024 * 1024)]
    [InlineData(false, 50L * 1024 * 1024, AiOptions.MaxPdfInputMaxBytes)]
    public void InputSettings_PdfByteCapDefault_FollowsPdfUpload(bool upload, long? maxBytes, long expected)
    {
        var settings = AiInputSettings.From(new AiOptions { PdfUpload = upload, PdfInputMaxBytes = maxBytes });

        Assert.Equal(expected, settings.PdfInputMaxBytes);
    }

    [Fact]
    public void PdfUpload_DefaultsOn_AndBindsOff()
    {
        Assert.True(Bind(Config()).PdfUpload);
        Assert.False(Bind(Config(("Ai:PdfUpload", "false"))).PdfUpload);
    }

    [Fact]
    public void InputSettings_ApplyFloorsAndCeiling()
    {
        var settings = AiInputSettings.From(new AiOptions
        {
            MaxInputChars = 10, StatementMaxInputChars = 500, ImageRenderMaxEdge = 10_000, Effort = "low",
        });

        Assert.Equal(AiEffort.Low, settings.Effort);
        Assert.Equal(AiOptions.DefaultMaxInputChars, settings.MaxInputChars);
        Assert.Equal(AiOptions.DefaultStatementMaxInputChars, settings.StatementMaxInputChars);
        Assert.Equal(AiOptions.MaxImageRenderMaxEdge, settings.ImageRenderMaxEdge);
    }

    [Fact]
    public void InputSettings_KeepValuesAboveTheFloor()
    {
        var settings = AiInputSettings.From(new AiOptions
        {
            MaxInputChars = 5_000, StatementMaxInputChars = 50_000, ImageRenderMaxEdge = 512, ImageInput = false,
        });

        Assert.Equal(5_000, settings.MaxInputChars);
        Assert.Equal(50_000, settings.StatementMaxInputChars);
        Assert.Equal(512, settings.ImageRenderMaxEdge);
        Assert.False(settings.ImageInput);
    }

    [Fact]
    public void Provider_ValidatesOnceIntoAvailability()
    {
        var provider = new AnthropicAiProvider(new AiOptions { Model = "m" }, legacyKeysPresent: false,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AnthropicAiProvider>.Instance, new AiTelemetry());

        Assert.Equal(AiAvailability.Unavailable("Ai:ApiKey is required."), provider.Availability);
    }

    [Theory]
    [InlineData(false, "AI is not configured.")]
    [InlineData(true, "AI is unavailable: Ai:ApiKey is required.")]
    public async Task Provider_Unconfigured_ThrowsNamingTheReason(bool present, string expected)
    {
        var provider = new AnthropicAiProvider(present ? new AiOptions { Model = "m" } : new AiOptions(), false,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AnthropicAiProvider>.Instance, new AiTelemetry());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SendAsync(
            new AiRequest(AiFeature.ConnectionProbe, AiEffort.Low, "s", [new AiTextPart("t")], default,
                false, "Probe"),
            TestContext.Current.CancellationToken));
        Assert.Equal(expected, ex.Message);
    }
}
