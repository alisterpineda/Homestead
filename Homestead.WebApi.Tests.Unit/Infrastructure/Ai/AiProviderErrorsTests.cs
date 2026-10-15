using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using Anthropic.Exceptions;
using Homestead.WebApi.Infrastructure.Ai;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

public class AiProviderErrorsTests
{
    [Theory]
    [InlineData(400)]
    [InlineData(0)]
    public void OpenAiSdk_ReportsItsStatus(int status)
    {
        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(status);

        Assert.True(AiProviderErrors.TryGetStatus(
            new ClientResultException("json_schema not supported", response), out var actual, out var message));
        Assert.Equal(status, actual);
        Assert.Contains("json_schema", message);
    }

    // The message carries the response body — that is what the fallback ladder reads.
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

    [Fact]
    public void AnUnrelatedException_IsNotAProviderError()
    {
        Assert.False(AiProviderErrors.TryGetStatus(new InvalidOperationException("x"), out _, out _));
    }
}
