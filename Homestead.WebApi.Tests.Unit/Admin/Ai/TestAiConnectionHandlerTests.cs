using System.Net;
using System.Text.Json.Nodes;
using Anthropic.Exceptions;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;
using Homestead.WebApi.Tests.Unit.Infrastructure.Ai;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Homestead.WebApi.Tests.Unit.Admin.Ai;

public class TestAiConnectionHandlerTests
{
    private readonly IAiProvider _provider = TestAiProviders.Provider("""{"ok":true}""");

    private TestAiConnectionHandler Sut(AiInputSettings? settings = null) =>
        new(_provider, settings ?? TestAiProviders.Settings());

    [Fact]
    public async Task AiAbsent_ReturnsFailure_WithoutSending()
    {
        var ct = TestContext.Current.CancellationToken;
        var absent = TestAiProviders.Absent();

        var result = await new TestAiConnectionHandler(absent, TestAiProviders.Settings()).HandleAsync(ct);

        Assert.False(result.Success);
        Assert.Equal("AI is not configured.", result.Message);
        Assert.Null(result.LatencyMs);
        await absent.DidNotReceive().SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>());
    }

    // Config that is there but cannot run: the probe reports the reason instead of sending a request that would throw.
    [Fact]
    public async Task AiUnavailable_ReturnsFailureWithReason_WithoutSending()
    {
        var ct = TestContext.Current.CancellationToken;
        var broken = TestAiProviders.Unconfigured("Ai:ApiKey is required.");

        var result = await new TestAiConnectionHandler(broken, TestAiProviders.Settings()).HandleAsync(ct);

        Assert.False(result.Success);
        Assert.Equal("AI is unavailable: Ai:ApiKey is required.", result.Message);
        Assert.Null(result.LatencyMs);
        await broken.DidNotReceive().SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HappyPath_ReturnsSuccess_WithLatency()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Sut().HandleAsync(ct);

        Assert.True(result.Success);
        Assert.Equal("Connection successful.", result.Message);
        Assert.NotNull(result.LatencyMs);
        Assert.True(result.LatencyMs >= 0);
    }

    // The probe is a real structured-output request, not a bare "ping": the same strict schema and the configured
    // effort every AI service sends, so a model that rejects either fails here, at setup time. Buffered — the reply
    // is a few tokens.
    [Fact]
    public async Task Probe_SendsAStrictSchema_AtTheConfiguredEffort_Buffered()
    {
        var ct = TestContext.Current.CancellationToken;
        _provider.Capture(out var sent);

        var result = await Sut(TestAiProviders.Settings(effort: AiEffort.Low)).HandleAsync(ct);

        Assert.True(result.Success);
        var request = sent();
        Assert.NotNull(request);
        Assert.Equal(AiFeature.ConnectionProbe, request.Feature);
        Assert.Equal(AiEffort.Low, request.Effort);
        Assert.False(request.LongOutput);
        Assert.IsType<AiTextPart>(Assert.Single(request.UserContent));
        var schema = JsonNode.Parse(request.Schema.GetRawText())!;
        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.Equal("boolean", schema["properties"]!["ok"]!["type"]!.GetValue<string>());
        Assert.Equal(["ok"], schema["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    // A provider 400 (a model that rejects the configured effort, an unknown model ID) is the point of the probe: it
    // comes back as a failed result carrying the provider's own account of what it rejected, for the operator to
    // read on /admin/ai.
    [Fact]
    public async Task Anthropic400_ReturnsFailure_WithTheProvidersMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var error = new AnthropicBadRequestException
        {
            StatusCode = HttpStatusCode.BadRequest,
            ResponseBody = """{"error":{"message":"effort: unsupported for this model"}}""",
        };
        _provider.SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(error);

        var result = await Sut().HandleAsync(ct);

        Assert.False(result.Success);
        Assert.Contains("effort: unsupported for this model", result.Message);
        Assert.Null(result.LatencyMs);
    }

    // A 200 that isn't an answer — declined, or cut off at the output cap — is not a working connection either.
    [Theory]
    [InlineData("AI connection test: the model declined the request (unspecified).")]
    [InlineData("AI connection test: the response hit the output-token limit and was cut off.")]
    public async Task IncompleteReply_ReturnsFailure(string message)
    {
        var ct = TestContext.Current.CancellationToken;
        _provider.SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AiResponseIncompleteException(message));

        var result = await Sut().HandleAsync(ct);

        Assert.False(result.Success);
        Assert.Equal(message, result.Message);
        Assert.Null(result.LatencyMs);
    }

    [Fact]
    public async Task ProviderThrows_ReturnsFailure_WithExceptionMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        _provider.SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("bad endpoint"));

        var result = await Sut().HandleAsync(ct);

        Assert.False(result.Success);
        Assert.Equal("bad endpoint", result.Message);
        Assert.Null(result.LatencyMs);
    }

    [Fact]
    public async Task ProbeTimeout_ReturnsFriendlyFailure()
    {
        // A cancellation NOT originating from the caller (i.e. our own probe timeout) is reported as a
        // friendly connection result, not propagated. The outer token is never cancelled here.
        var ct = TestContext.Current.CancellationToken;
        _provider.SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var result = await Sut().HandleAsync(ct);

        Assert.False(result.Success);
        Assert.Equal("Connection timed out.", result.Message);
        Assert.Null(result.LatencyMs);
    }
}
