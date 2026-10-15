using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using Anthropic.Exceptions;
using Azure;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

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

    [Theory]
    [InlineData(0, true)]
    [InlineData(429, true)]
    [InlineData(503, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    public void AiSdkFailures_AreClassifiedByStatus(int status, bool expected)
    {
        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(status);

        Assert.Equal(expected, JobFailureClassifier.IsTransient(new ClientResultException("x", response)));
    }

    [Fact]
    public void AnthropicSdkFailures_AreClassifiedByStatus()
    {
        Assert.True(JobFailureClassifier.IsTransient(
            new AnthropicRateLimitException { StatusCode = HttpStatusCode.TooManyRequests, ResponseBody = "" }));
        Assert.True(JobFailureClassifier.IsTransient(
            new Anthropic5xxException { StatusCode = HttpStatusCode.ServiceUnavailable, ResponseBody = "" }));
        Assert.False(JobFailureClassifier.IsTransient(
            new AnthropicBadRequestException { StatusCode = HttpStatusCode.BadRequest, ResponseBody = "" }));
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
