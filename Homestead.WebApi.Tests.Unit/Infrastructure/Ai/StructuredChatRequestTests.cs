using System.ClientModel;
using System.ClientModel.Primitives;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Exceptions;
using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

// The service-level ladder tests (AiClassificationServiceTests) cover the OpenAI SDK's 400 through a real
// service; these cover the provider-neutral parts: Anthropic's 400s, its parameter names, and the
// completeness check on a successful reply.
public class StructuredChatRequestTests
{
    private readonly IChatClient _client = Substitute.For<IChatClient>();
    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { title = new { type = "string" } },
        required = new[] { "title" },
        additionalProperties = false,
    });

    private Task<ChatResponse> Send(bool stream = false) => StructuredChatRequest.SendAsync(
        _client, [new ChatMessage(ChatRole.User, "hi")], Schema, "test_schema", "A test schema.",
        temperature: 0f, stream, "Test request", NullLogger.Instance, TestContext.Current.CancellationToken);

    private static ChatResponse Reply(string text = """{"title":"x"}""", ChatFinishReason? finishReason = null) =>
        new(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = finishReason };

    // The Anthropic SDK's Message is "Status Code: …\n{body}", so the body is what names the rejected option.
    private static AnthropicBadRequestException AnthropicBadRequest(string body) =>
        new() { StatusCode = HttpStatusCode.BadRequest, ResponseBody = body };

    private static bool IsStructuredOutput(ChatOptions o) =>
        o.ResponseFormat is ChatResponseFormatJson { Schema: not null };

    private static bool IsJsonMode(ChatOptions o) =>
        o.ResponseFormat is ChatResponseFormatJson { Schema: null };

    [Fact]
    public async Task AnthropicTemperature400_RetriesWithoutTemperature_KeepingStructuredOutput()
    {
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Is<ChatOptions>(o => o.Temperature != null), Arg.Any<CancellationToken>())
            .ThrowsAsync(AnthropicBadRequest("""{"error":{"message":"temperature: unsupported for this model"}}"""));
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Is<ChatOptions>(o => o.Temperature == null && IsStructuredOutput(o)), Arg.Any<CancellationToken>())
            .Returns(Reply());

        var response = await Send();

        Assert.Equal("""{"title":"x"}""", response.Text);
    }

    [Fact]
    public async Task AnthropicOutputConfig400_FallsBackToJsonMode_KeepingTemperature()
    {
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Is<ChatOptions>(o => IsStructuredOutput(o)), Arg.Any<CancellationToken>())
            .ThrowsAsync(AnthropicBadRequest("""{"error":{"message":"output_config.format: schema not supported"}}"""));
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Is<ChatOptions>(o => IsJsonMode(o) && o.Temperature == 0f), Arg.Any<CancellationToken>())
            .Returns(Reply());

        await Send();

        await _client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => IsJsonMode(o) && o.Temperature == 0f), Arg.Any<CancellationToken>());
    }

    // A 400 that implicates neither optional parameter is the real error — never retried, never masked.
    [Fact]
    public async Task AnthropicUnrelated400_Propagates_WithoutRetry()
    {
        var error = AnthropicBadRequest("""{"error":{"message":"prompt is too long"}}""");
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(error);

        var thrown = await Assert.ThrowsAsync<AnthropicBadRequestException>(() => Send());

        Assert.Same(error, thrown);
        await _client.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenAiSchema400_StillFallsBackToJsonMode()
    {
        var pipelineResponse = Substitute.For<PipelineResponse>();
        pipelineResponse.Status.Returns(400);
        // Built before the stub: its constructor reads Status, which NSubstitute would mistake for the call
        // being configured.
        var schemaUnsupported = new ClientResultException("json_schema response format not supported", pipelineResponse);
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Is<ChatOptions>(o => IsStructuredOutput(o)), Arg.Any<CancellationToken>())
            .ThrowsAsync(schemaUnsupported);
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Is<ChatOptions>(o => IsJsonMode(o)), Arg.Any<CancellationToken>())
            .Returns(Reply());

        Assert.Equal("""{"title":"x"}""", (await Send()).Text);
    }

    // A non-400 provider error (rate limit) is not the ladder's business.
    [Fact]
    public async Task AnthropicNon400_Propagates_WithoutRetry()
    {
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AnthropicRateLimitException
            {
                StatusCode = HttpStatusCode.TooManyRequests, ResponseBody = "temperature",
            });

        await Assert.ThrowsAsync<AnthropicRateLimitException>(() => Send());
        await _client.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("content_filter", "declined the request")]
    [InlineData("length", "output-token limit")]
    public async Task IncompleteReply_ThrowsNamingTheRequest(string finishReason, string expected)
    {
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Reply("""{"tit""", new ChatFinishReason(finishReason)));

        var ex = await Assert.ThrowsAsync<AiResponseIncompleteException>(() => Send());

        Assert.StartsWith("Test request:", ex.Message);
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Stream_UsesTheStreamingCall_AndChecksCompleteness()
    {
        _client.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Updates(
                new ChatResponseUpdate(ChatRole.Assistant, """{"title":"""),
                new ChatResponseUpdate(ChatRole.Assistant, "\"x\"}") { FinishReason = ChatFinishReason.Stop }));

        var response = await Send(stream: true);

        Assert.Equal("""{"title":"x"}""", response.Text);
        await _client.DidNotReceive().GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stream_CutOffReply_Throws()
    {
        _client.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Updates(
                new ChatResponseUpdate(ChatRole.Assistant, """{"tit""") { FinishReason = ChatFinishReason.Length }));

        await Assert.ThrowsAsync<AiResponseIncompleteException>(() => Send(stream: true));
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Updates(
        params ChatResponseUpdate[] updates)
    {
        foreach (var update in updates)
        {
            await Task.Yield();
            yield return update;
        }
    }

    // CreateStrictSchema: every object closed and fully required, optionality as a ["type","null"] union, and a
    // nullable enum rewritten to anyOf (Anthropic rejects ["string","null"] + enum) without losing its siblings.

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static JsonNode StrictSchema() =>
        JsonNode.Parse(StructuredChatRequest.CreateStrictSchema(typeof(SchemaShapes), WebJson).GetRawText())!;

    private static void AssertProperty(string name, string expectedJson)
    {
        var actual = StrictSchema()["properties"]![name];
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectedJson), actual),
            $"Schema for '{name}' was:\n{actual?.ToJsonString()}");
    }

    [Fact]
    public void CreateStrictSchema_ClosesAndRequiresEveryProperty()
    {
        var schema = StrictSchema();

        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            ["nullableEnum", "describedNullableEnum", "requiredEnum", "nullableText"],
            schema["required"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void CreateStrictSchema_WritesANullableEnumAsAnyOf() =>
        AssertProperty("nullableEnum", """
            { "anyOf": [{ "type": "string", "enum": ["out", "in"] }, { "type": "null" }] }
            """);

    [Fact]
    public void CreateStrictSchema_KeepsTheDescriptionOfARewrittenNullableEnum() =>
        AssertProperty("describedNullableEnum", """
            {
              "description": "Which way the money moved.",
              "anyOf": [{ "type": "string", "enum": ["out", "in"] }, { "type": "null" }]
            }
            """);

    [Fact]
    public void CreateStrictSchema_LeavesANonNullableEnumAlone() =>
        AssertProperty("requiredEnum", """{ "type": "string", "enum": ["Bank", "Unknown"] }""");

    [Fact]
    public void CreateStrictSchema_LeavesANullableNonEnumAlone() =>
        AssertProperty("nullableText", """{ "type": ["string", "null"] }""");

    private sealed class SchemaShapes
    {
        [AllowedValues("out", "in")]
        public string? NullableEnum { get; set; }

        [AllowedValues("out", "in")]
        [Description("Which way the money moved.")]
        public string? DescribedNullableEnum { get; set; }

        [AllowedValues("Bank", "Unknown")]
        public string RequiredEnum { get; set; } = "";

        public string? NullableText { get; set; }
    }
}
