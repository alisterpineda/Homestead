using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

// What the provider reads out of a Claude reply — text, served model, billed usage, stop reason — and the
// completeness allow-list, driven with SDK types built from wire JSON (no HTTP).
public class AnthropicReplyMapperTests
{
    private static BetaMessage Message(
        string stopReason = "end_turn",
        string content = """[{ "type": "text", "text": "{\"title\":\"x\"}", "citations": null }]""",
        string stopDetails = "null",
        string model = "claude-opus-5-5",
        string usage = """{ "input_tokens": 100, "output_tokens": 20 }""") =>
        BetaMessage.FromRawUnchecked(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>($$"""
            {
              "id": "msg_1", "type": "message", "role": "assistant", "model": "{{model}}",
              "content": {{content}},
              "stop_reason": "{{stopReason}}", "stop_sequence": null, "stop_details": {{stopDetails}},
              "container": null, "context_management": null,
              "usage": {{usage}}
            }
            """)!);

    [Fact]
    public void Text_SkipsThinkingBlocks()
    {
        var reply = AnthropicReplyMapper.From(Message(content: """
            [
              { "type": "thinking", "thinking": "", "signature": "sig" },
              { "type": "text", "text": "{\"title\":", "citations": null },
              { "type": "text", "text": "\"x\"}", "citations": null }
            ]
            """));

        Assert.Equal("""{"title":"x"}""", reply.Json);
        Assert.Equal("end_turn", reply.StopReason);
        Assert.False(reply.FallbackServed);
    }

    [Fact]
    public void Usage_ReadsEveryCount_IncludingThinkingAndCache()
    {
        var reply = AnthropicReplyMapper.From(Message(usage: """
            {
              "input_tokens": 100, "output_tokens": 20,
              "cache_read_input_tokens": 7, "cache_creation_input_tokens": 3,
              "output_tokens_details": { "thinking_tokens": 12 }
            }
            """));

        Assert.Equal(new AiUsage(100, 20, 12, 7, 3), reply.Usage);
    }

    // After a fallback the top-level usage covers only the serving attempt; the iterations list every billed one.
    [Fact]
    public void Fallback_SumsIterations_AndReportsTheServedModel()
    {
        var reply = AnthropicReplyMapper.From(Message(
            model: "claude-opus-4-6",
            usage: """
                {
                  "input_tokens": 120, "output_tokens": 30,
                  "output_tokens_details": { "thinking_tokens": 5 },
                  "iterations": [
                    { "type": "message", "input_tokens": 100, "output_tokens": 4,
                      "cache_read_input_tokens": 0, "cache_creation_input_tokens": 0 },
                    { "type": "fallback_message", "model": "claude-opus-4-6", "input_tokens": 120, "output_tokens": 30,
                      "cache_read_input_tokens": 2, "cache_creation_input_tokens": 1, "cache_creation": null }
                  ]
                }
                """));

        Assert.Equal("claude-opus-4-6", reply.Model);
        Assert.True(reply.FallbackServed);
        Assert.Equal(new AiUsage(220, 34, 5, 2, 1), reply.Usage);
    }

    [Fact]
    public void Complete_OnlyForEndTurn()
    {
        AnthropicReplyMapper.EnsureComplete(AnthropicReplyMapper.From(Message()), "Test request");
    }

    [Theory]
    [InlineData("max_tokens", "Test request: the response hit the output-token limit and was cut off.")]
    [InlineData("model_context_window_exceeded", "Test request: the model stopped early (model_context_window_exceeded).")]
    [InlineData("pause_turn", "Test request: the model stopped early (pause_turn).")]
    [InlineData("some_future_reason", "Test request: the model stopped early (some_future_reason).")]
    public void Incomplete_ThrowsNamingTheRequest(string stopReason, string expected)
    {
        var reply = AnthropicReplyMapper.From(Message(stopReason));

        var ex = Assert.Throws<AiResponseIncompleteException>(() => AnthropicReplyMapper.EnsureComplete(reply, "Test request"));
        Assert.Equal(expected, ex.Message);
    }

    [Theory]
    [InlineData("\"cyber\"", "cyber")]
    [InlineData("null", "unspecified")]
    public void Refusal_ThrowsNamingTheCategory(string category, string expected)
    {
        var reply = AnthropicReplyMapper.From(Message("refusal", content: "[]", stopDetails: $$"""
            { "type": "refusal", "category": {{category}}, "explanation": null,
              "fallback_credit_token": null, "fallback_has_prefill_claim": null, "recommended_model": null }
            """));

        var ex = Assert.Throws<AiResponseIncompleteException>(() => AnthropicReplyMapper.EnsureComplete(reply, "Test request"));
        Assert.Equal($"Test request: the model declined the request ({expected}).", ex.Message);
    }

    // The streamed path assembles events into the same BetaMessage, so the one mapper covers it.
    [Fact]
    public async Task StreamedEvents_AggregateToTheSameReply()
    {
        string[] events =
        [
            """{ "type": "message_start", "message": { "id": "msg_1", "type": "message", "role": "assistant", "model": "claude-opus-5-5", "content": [], "stop_reason": null, "stop_sequence": null, "stop_details": null, "container": null, "context_management": null, "usage": { "input_tokens": 50, "output_tokens": 1 } } }""",
            """{ "type": "content_block_start", "index": 0, "content_block": { "type": "text", "text": "", "citations": null } }""",
            """{ "type": "content_block_delta", "index": 0, "delta": { "type": "text_delta", "text": "{\"title\":" } }""",
            """{ "type": "content_block_delta", "index": 0, "delta": { "type": "text_delta", "text": "\"x\"}" } }""",
            """{ "type": "content_block_stop", "index": 0 }""",
            """{ "type": "message_delta", "delta": { "stop_reason": "end_turn", "stop_sequence": null, "stop_details": null, "container": null }, "context_management": null, "usage": { "input_tokens": 50, "output_tokens": 9, "cache_read_input_tokens": null, "cache_creation_input_tokens": null, "server_tool_use": null } }""",
            """{ "type": "message_stop" }""",
        ];

        var message = await Stream(events).Aggregate();
        var reply = AnthropicReplyMapper.From(message);

        Assert.Equal("""{"title":"x"}""", reply.Json);
        Assert.Equal("end_turn", reply.StopReason);
        Assert.Equal(9, reply.Usage.OutputTokens);
    }

    private static async IAsyncEnumerable<BetaRawMessageStreamEvent> Stream(string[] events)
    {
        foreach (var e in events)
        {
            await Task.Yield();
            yield return JsonSerializer.Deserialize<BetaRawMessageStreamEvent>(e)!;
        }
    }
}
