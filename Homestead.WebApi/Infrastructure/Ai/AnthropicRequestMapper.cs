using System.Text.Json;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

namespace Homestead.WebApi.Infrastructure.Ai;

// AiRequest -> the Claude API's Beta messages request. Pure (no HTTP), so the exact request shape is unit-tested.
// The Beta endpoint because the GA request type has no Fallbacks/Betas (the server-side refusal fallback), and it
// is where new Claude capabilities land first. Only this file, the reply mapper and the provider see SDK types.
internal static class AnthropicRequestMapper
{
    // The Claude API requires max_tokens on every request, and thinking (always on for current Opus models) counts
    // against it. The buffered cap stays modest because a non-streamed reply must arrive within the SDK's
    // non-streaming timeout (30s per 1000 max_tokens, at most 10 minutes); a request that may need more streams,
    // with a cap sized for current models' 128K output. Older models with lower output limits are not
    // accommodated: such a model fails with an error naming max_tokens rather than truncating silently.
    public const int BufferedMaxTokens = 16_000;
    public const int StreamedMaxTokens = 64_000;

    // Stream for a long reply (statement line items grow with the transaction count), and at the top efforts,
    // where thinking alone can exhaust the buffered cap. Streaming defeats a timeout; tokens are not consumed
    // incrementally.
    public static bool ShouldStream(AiRequest request) =>
        request.LongOutput || request.Effort is AiEffort.XHigh or AiEffort.Max;

    public static MessageCreateParams ToParams(AiRequest request, AiOptions options)
    {
        var stream = ShouldStream(request);

        // No Temperature (current Claude models reject sampling parameters), no Thinking (adaptive is the model
        // default; budget_tokens and "disabled" are 400s), no Citations (they don't combine with a format).
        var parameters = new MessageCreateParams
        {
            Model = options.Model!,
            MaxTokens = stream ? StreamedMaxTokens : BufferedMaxTokens,
            System = request.SystemPrompt,
            Messages = [new BetaMessageParam { Role = Role.User, Content = ToContent(request.UserContent) }],
            OutputConfig = new BetaOutputConfig
            {
                // The wire string through ApiEnum's implicit conversion, rather than guessing the enum member's casing.
                Effort = AiConfiguration.ToWire(request.Effort),
                Format = new BetaJsonOutputFormat { Schema = ToSchemaDictionary(request.Schema) },
            },
        };

        // Set only when on: assigning null would send an explicit "fallbacks": null rather than omitting the field.
        // "default" lets Anthropic route a refusal by category to a model that doesn't run that classifier; the
        // -07-01 beta is the scalar form's header (-06-01 is the array form, and the two don't mix).
        return options.RefusalFallback
            ? parameters with { Fallbacks = new Default(), Betas = [AnthropicBeta.ServerSideFallback2026_07_01] }
            : parameters;
    }

    // Documents and images go before the text that refers to them (the Claude API's documented best order),
    // whatever order the caller listed them in; relative order within each kind is kept.
    private static List<BetaContentBlockParam> ToContent(IReadOnlyList<AiContentPart> parts)
    {
        var blocks = new List<BetaContentBlockParam>(parts.Count);
        foreach (var part in parts.OfType<AiPdfPart>())
            blocks.Add(new BetaRequestDocumentBlock
            {
                Source = new BetaBase64PdfSource { Data = Convert.ToBase64String(part.Bytes) },
            });
        foreach (var part in parts.OfType<AiImagePart>())
            blocks.Add(new BetaImageBlockParam
            {
                Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(part.Bytes), MediaType = part.MediaType },
            });
        foreach (var part in parts.OfType<AiTextPart>())
            blocks.Add(new BetaTextBlockParam { Text = part.Text });
        return blocks;
    }

    private static Dictionary<string, JsonElement> ToSchemaDictionary(JsonElement schema) =>
        schema.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
}
