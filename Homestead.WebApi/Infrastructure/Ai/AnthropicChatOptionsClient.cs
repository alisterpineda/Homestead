using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Infrastructure.Ai;

// Adapts provider-neutral ChatOptions to what the Claude API accepts, so callers stay unaware of the provider
// behind their IChatClient. Wraps the Anthropic SDK's own IChatClient; the caller's ChatOptions instance is
// never mutated (the fallback ladder builds its options per attempt, but other callers may reuse theirs).
public sealed class AnthropicChatOptionsClient(IChatClient inner) : DelegatingChatClient(inner)
{
    // Anthropic requires max_tokens on every request and the SDK adapter sends 1024 when MaxOutputTokens is
    // unset — which it always is here — and thinking (always on for current Opus models) counts against it, so
    // replies would be cut off. The caps are sized for current Claude models (128K output): the buffered one
    // stays modest because a non-streamed reply must arrive within the SDK's non-streaming timeout (30s per
    // 1000 max_tokens, at most 10 minutes), and statement
    // extraction streams because its line-item output grows with the transaction count. Older models have
    // lower limits and are not accommodated: the SDK refuses a non-streaming request above 8192 for the
    // Claude Opus 4 / 4.1 IDs, and Opus 4 rejects 64000 outright — such a model fails with an error naming
    // max_tokens rather than truncating silently.
    public const int NonStreamingMaxOutputTokens = 16_000;
    public const int StreamingMaxOutputTokens = 64_000;

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, Adapt(options, NonStreamingMaxOutputTokens), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, Adapt(options, StreamingMaxOutputTokens), cancellationToken);

    private static ChatOptions Adapt(ChatOptions? options, int defaultMaxOutputTokens)
    {
        var adapted = options?.Clone() ?? new ChatOptions();

        // Current Claude models reject any non-default sampling parameter with an HTTP 400. Dropping it here
        // rather than letting the fallback ladder react to the 400 saves a wasted round-trip on every call.
        adapted.Temperature = null;
        adapted.MaxOutputTokens ??= defaultMaxOutputTokens;

        return adapted;
    }
}
