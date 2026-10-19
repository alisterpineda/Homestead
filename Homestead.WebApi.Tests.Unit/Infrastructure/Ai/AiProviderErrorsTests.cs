using System.Net;
using Anthropic.Exceptions;
using Anthropic.Models;
using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

public class AiProviderErrorsTests
{
    // The message carries the response body — the provider's own account of what it rejected.
    [Fact]
    public void AnthropicBadRequest_ReportsStatusAndBody()
    {
        var ex = new AnthropicBadRequestException
        {
            StatusCode = HttpStatusCode.BadRequest, ResponseBody = """{"error":{"message":"temperature"}}""",
        };

        Assert.True(AiProviderErrors.TryGetStatus(ex, out var status, out var message));
        Assert.Equal(400, status);
        Assert.Contains("temperature", message);
    }

    [Fact]
    public void AnthropicRateLimit_ReportsItsStatus()
    {
        var ex = new AnthropicRateLimitException { StatusCode = HttpStatusCode.TooManyRequests, ResponseBody = "" };

        Assert.True(AiProviderErrors.TryGetStatus(ex, out var status, out _));
        Assert.Equal(429, status);
    }

    [Fact]
    public void AnthropicNetworkFailure_IsNoAnswer()
    {
        Assert.True(AiProviderErrors.TryGetStatus(
            new AnthropicIOException("connection refused", new HttpRequestException("refused")), out var status, out _));
        Assert.Equal(0, status);
    }

    // An error event after the HTTP 200 of a streamed reply: the event's type stands in for a status, so a mid-stream
    // overload still reads as transient.
    [Theory]
    [InlineData(ErrorType.OverloadedError, 529)]
    [InlineData(ErrorType.ApiError, 500)]
    [InlineData(ErrorType.RateLimitError, 429)]
    [InlineData(ErrorType.InvalidRequestError, 0)]
    [InlineData(null, 0)]
    public void AnthropicStreamErrorEvent_ReportsAStatusFromItsType(ErrorType? type, int expected)
    {
        var ex = new AnthropicSseException("Overloaded") { ErrorType = type };

        Assert.True(AiProviderErrors.TryGetStatus(ex, out var status, out var message));
        Assert.Equal(expected, status);
        Assert.Contains("Overloaded", message);
    }

    [Fact]
    public void AnthropicCutOffStream_IsNoAnswer()
    {
        Assert.True(AiProviderErrors.TryGetStatus(
            new AnthropicInvalidDataException("stream ended early"), out var status, out _));
        Assert.Equal(0, status);
    }

    [Fact]
    public void AnUnrelatedException_IsNotAProviderError()
    {
        Assert.False(AiProviderErrors.TryGetStatus(new InvalidOperationException("x"), out _, out _));
    }
}
