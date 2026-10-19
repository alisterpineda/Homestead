using System.Text;
using Anthropic.Models.Beta.Messages;

namespace Homestead.WebApi.Infrastructure.Ai;

// The Claude API's Beta message -> what the provider needs: the reply text, the served model, the billed usage,
// and why the model stopped. Pure (no HTTP); the streamed path assembles the same BetaMessage first, so one mapper
// covers both. The stop reason is internal to the provider: consumers only ever see a complete reply or an
// AiResponseIncompleteException.
internal static class AnthropicReplyMapper
{
    internal sealed record MappedReply(
        string Json, string Model, AiUsage Usage, string StopReason, string? RefusalCategory, bool FallbackServed);

    public static MappedReply From(BetaMessage message)
    {
        // Thinking blocks come first (empty text by default on current models) and are skipped; a fallback block
        // only marks the switch point.
        var text = new StringBuilder();
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var textBlock))
                text.Append(textBlock.Text);
        }

        var refusalCategory = message.StopDetails is { } details
            ? details.Category?.Raw() ?? "unspecified"
            : null;

        return new MappedReply(
            text.ToString(),
            message.Model.Raw(),
            ToUsage(message.Usage),
            message.StopReason?.Raw() ?? "unknown",
            refusalCategory,
            FallbackServed(message));
    }

    // An allow-list: only end_turn is a complete answer. A deny-list would hand truncated JSON to the parser for
    // any stop reason it didn't name, which the classify job retries as if it were malformed model output — three
    // paid attempts for a deterministic failure.
    public static bool IsComplete(MappedReply reply) => reply.StopReason == "end_turn";

    public static void EnsureComplete(MappedReply reply, string requestLabel)
    {
        if (IsComplete(reply))
            return;

        switch (reply.StopReason)
        {
            // Reachable only with RefusalFallback off, or when the fallback model declined too.
            case "refusal":
                throw new AiResponseIncompleteException(
                    $"{requestLabel}: the model declined the request ({reply.RefusalCategory ?? "unspecified"}).");
            case "max_tokens":
                throw new AiResponseIncompleteException(
                    $"{requestLabel}: the response hit the output-token limit and was cut off.");
            // model_context_window_exceeded, stop_sequence, pause_turn, tool_use, and any future value.
            default:
                throw new AiResponseIncompleteException($"{requestLabel}: the model stopped early ({reply.StopReason}).");
        }
    }

    // SDK counts are long and partly nullable. When a fallback served the reply, the top-level usage covers only the
    // serving attempt, while usage.iterations lists every attempt — so the sum over iterations is what was billed.
    // Thinking tokens are a breakdown of output and exist only at the top level.
    private static AiUsage ToUsage(BetaUsage usage)
    {
        var thinking = usage.OutputTokensDetails?.ThinkingTokens ?? 0;

        if (usage.Iterations is { Count: > 0 } iterations && iterations.Any(IsFallbackIteration))
        {
            long input = 0, output = 0, cacheRead = 0, cacheCreation = 0;
            foreach (var iteration in iterations)
            {
                var (i, o, r, c) = iteration.Match(
                    m => (m.InputTokens, m.OutputTokens, m.CacheReadInputTokens, m.CacheCreationInputTokens),
                    compaction => (compaction.InputTokens, compaction.OutputTokens, compaction.CacheReadInputTokens,
                        compaction.CacheCreationInputTokens),
                    advisor => (advisor.InputTokens, advisor.OutputTokens, advisor.CacheReadInputTokens,
                        advisor.CacheCreationInputTokens),
                    fallback => (fallback.InputTokens, fallback.OutputTokens, fallback.CacheReadInputTokens,
                        fallback.CacheCreationInputTokens));
                input += i;
                output += o;
                cacheRead += r;
                cacheCreation += c;
            }
            return new AiUsage(input, output, thinking, cacheRead, cacheCreation);
        }

        return new AiUsage(
            usage.InputTokens,
            usage.OutputTokens,
            thinking,
            usage.CacheReadInputTokens ?? 0,
            usage.CacheCreationInputTokens ?? 0);
    }

    private static bool IsFallbackIteration(BetaUsageIteration iteration) =>
        iteration.TryPickBetaFallbackMessageIterationUsage(out _);

    // A fallback block marks a switch inside this reply; a fallback iteration also covers a reply whose fallback
    // carried no block. Either one means the configured model did not serve it.
    private static bool FallbackServed(BetaMessage message) =>
        message.Content.Any(b => b.TryPickFallback(out _))
        || message.Usage.Iterations?.Any(IsFallbackIteration) == true;
}
