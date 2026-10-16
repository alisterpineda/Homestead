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
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

// The provider-neutral request path: what it asks for (the strict schema, no temperature), that it makes one
// attempt and lets every provider error through untouched (Anthropic's and the OpenAI SDK's alike), and the
// completeness check on a successful reply. What the provider pipeline then does to those options is
// AiProviderTests' concern.
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
        stream, "Test request", TestContext.Current.CancellationToken);

    private static ChatResponse Reply(string text = """{"title":"x"}""", ChatFinishReason? finishReason = null) =>
        new(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = finishReason };

    // The Anthropic SDK's Message is "Status Code: …\n{body}", so the body is what names the rejected option.
    private static AnthropicBadRequestException AnthropicBadRequest(string body) =>
        new() { StatusCode = HttpStatusCode.BadRequest, ResponseBody = body };

    // Callers always ask for exactly what they want — the schema, enforced, and no temperature. Adapting that to
    // what an endpoint accepts (a configured temperature, JSON mode for one without json_schema) is the provider
    // pipeline's job (AiProvider.BuildPipeline), never this request's.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sends_TheStrictSchema_AndNoTemperature(bool stream)
    {
        ChatOptions? sent = null;
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Do<ChatOptions>(o => sent = o), Arg.Any<CancellationToken>())
            .Returns(Reply());
        _client.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Do<ChatOptions>(o => sent = o), Arg.Any<CancellationToken>())
            .Returns(_ => Updates(new ChatResponseUpdate(ChatRole.Assistant, """{"title":"x"}""")));

        await Send(stream);

        var format = Assert.IsType<ChatResponseFormatJson>(sent!.ResponseFormat);
        Assert.True(JsonElement.DeepEquals(Schema, format.Schema!.Value));
        Assert.Equal("test_schema", format.SchemaName);
        Assert.Equal("A test schema.", format.SchemaDescription);
        Assert.Equal(true, sent.AdditionalProperties?["strict"]);
        Assert.Null(sent.Temperature);
    }

    // Every 400 is the provider's verdict on the request as configured, and goes back to the caller as-is after a
    // single attempt — including the ones that name temperature or the schema format, which the request used to
    // retry without. A wrong Temperature/StructuredOutput in config now fails loudly instead of degrading quietly.
    [Theory]
    [InlineData("""{"error":{"message":"temperature: unsupported for this model"}}""")]
    [InlineData("""{"error":{"message":"output_config.format: schema not supported"}}""")]
    [InlineData("""{"error":{"message":"prompt is too long"}}""")]
    public async Task Anthropic400_Propagates_AfterOneAttempt(string body)
    {
        var error = AnthropicBadRequest(body);
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(error);

        var thrown = await Assert.ThrowsAsync<AnthropicBadRequestException>(() => Send());

        Assert.Same(error, thrown);
        await _client.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenAiSchema400_Propagates_AfterOneAttempt()
    {
        var pipelineResponse = Substitute.For<PipelineResponse>();
        pipelineResponse.Status.Returns(400);
        // Built before the stub: its constructor reads Status, which NSubstitute would mistake for the call
        // being configured.
        var schemaUnsupported = new ClientResultException("json_schema response format not supported", pipelineResponse);
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(schemaUnsupported);

        var thrown = await Assert.ThrowsAsync<ClientResultException>(() => Send());

        Assert.Same(schemaUnsupported, thrown);
        await _client.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    // A non-400 provider error (rate limit) propagates the same way; retrying it is the job queue's call.
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
