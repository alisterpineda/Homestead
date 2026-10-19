using System.Net;
using Anthropic.Exceptions;
using Anthropic.Models;
using Azure;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class JobFailureClassifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void HttpFailures_ThatMayPass_AreTransient(HttpStatusCode status)
    {
        Assert.True(JobFailureClassifier.IsTransient(new HttpRequestException("x", null, status)));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public void HttpFailures_ThatWillRecur_AreNot(HttpStatusCode status)
    {
        Assert.False(JobFailureClassifier.IsTransient(new HttpRequestException("x", null, status)));
    }

    [Fact]
    public void AnHttpRequestThatGotNoAnswer_IsTransient()
    {
        Assert.True(JobFailureClassifier.IsTransient(new HttpRequestException("Connection refused")));
    }

    // 529 is Anthropic's "overloaded" — a 5xx like any other, so it retries.
    [Theory]
    [InlineData(400, false)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(529, true)]
    public void AnthropicSdkFailures_AreClassifiedByStatus(int status, bool expected)
    {
        AnthropicApiException exception = status switch
        {
            400 => new AnthropicBadRequestException { StatusCode = HttpStatusCode.BadRequest, ResponseBody = "" },
            429 => new AnthropicRateLimitException { StatusCode = HttpStatusCode.TooManyRequests, ResponseBody = "" },
            _ => new Anthropic5xxException { StatusCode = (HttpStatusCode)status, ResponseBody = "" },
        };

        Assert.Equal(expected, JobFailureClassifier.IsTransient(exception));
    }

    // An overload reported as an error event mid-stream (after the HTTP 200) has no status of its own; its error
    // type stands in for one, so it retries like the 529 it is.
    [Fact]
    public void AnAnthropicMidStreamOverload_IsTransient()
    {
        Assert.True(JobFailureClassifier.IsTransient(
            new AnthropicSseException("Overloaded") { ErrorType = ErrorType.OverloadedError }));
    }

    // A stream cut off part-way got no usable answer — the same "no answer at all" as a refused connection.
    [Fact]
    public void AnAnthropicCutOffStream_IsTransient()
    {
        Assert.True(JobFailureClassifier.IsTransient(new AnthropicInvalidDataException("stream ended early")));
    }

    [Fact]
    public void AnAnthropicRequestThatGotNoAnswer_IsTransient()
    {
        Assert.True(JobFailureClassifier.IsTransient(
            new AnthropicIOException("connection refused", new HttpRequestException("refused"))));
    }

    // A refusal or a cut-off reply is the model's answer, not a flaky transport — another attempt gets the same.
    [Fact]
    public void AnIncompleteModelReply_IsNot()
    {
        Assert.False(JobFailureClassifier.IsTransient(new AiResponseIncompleteException("declined")));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(500, true)]
    [InlineData(404, false)]
    public void AzureSdkFailures_AreClassifiedByStatus(int status, bool expected)
    {
        Assert.Equal(expected, JobFailureClassifier.IsTransient(new RequestFailedException(status, "x")));
    }

    [Fact]
    public void AnHttpClientTimeout_IsTransient()
    {
        Assert.True(JobFailureClassifier.IsTransient(new TaskCanceledException("The request timed out")));
    }

    [Fact]
    public void TheJobsOwnTimeout_IsNot()
    {
        // Another attempt gets exactly the same budget.
        Assert.False(JobFailureClassifier.IsTransient(new JobTimeoutException(Guid.NewGuid(), TimeSpan.FromMinutes(5))));
    }

    [Fact]
    public void DeterministicFailures_AreNot()
    {
        Assert.False(JobFailureClassifier.IsTransient(new InvalidOperationException("Job payload is missing or invalid.")));
        Assert.False(JobFailureClassifier.IsTransient(new FileNotFoundException("gone")));
    }

    [Fact]
    public void AStructuredError_IsFinal_EvenWhenItWrapsATransientOne()
    {
        var wrapped = new StructuredFailure(new HttpRequestException("x", null, HttpStatusCode.ServiceUnavailable));

        Assert.False(JobFailureClassifier.IsTransient(wrapped));
    }

    [Fact]
    public void ATransientCause_IsFoundThroughWrappers()
    {
        var wrapped = new DbUpdateException("save failed", new TimeoutException("socket timeout"));

        Assert.True(JobFailureClassifier.IsTransient(wrapped));
    }

    private sealed class StructuredFailure(Exception inner) : Exception("structured", inner), IStructuredJobError
    {
        public string ErrorCode => "some_code";
        public Dictionary<string, string> ErrorData => [];
    }
}
