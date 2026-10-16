using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;

// Live probe of one configured AI provider: sends a tiny structured-output request through
// StructuredChatRequest.SendAsync — the same strict json_schema request, provider pipeline and options every AI
// service sends — and reports whether the provider accepted and answered it. More than reachability: which
// options an endpoint accepts is stated in config (Ai:Providers:{key}:Temperature / StructuredOutput) and nothing
// retries around a mismatch, so this is where a wrong setting fails loudly, at setup time, with the provider's
// own 400 message shown on /admin/ai instead of failing every real job later.
//
// Deliberately write-free: the endpoint is a POST, so the command transaction filter wraps it, but the
// transaction opens lazily on the first flush with work. A write here would open it and pin a pooled
// connection for the duration of the slow external call (the idle-in-transaction shape the lazy-tx
// work fights).
public class TestAiConnectionHandler(IAiProviderResolver providerResolver)
{
    // Generous on purpose: the real classification path (AiClassificationService) passes the job's token
    // straight to StructuredChatRequest.SendAsync with no time cap, so a slow-but-working self-hosted model can
    // take far longer than a few seconds for even this one-field reply. A short probe cap would report "timed out" (reads as
    // broken) on a model that classification actually drives fine. 60s still bounds a genuinely hung endpoint.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(60);

    // Generated like the services' schemas — from the reply DTO, with the options a reply would be read with —
    // so the probe exercises CreateStrictSchema's shape too. The reply itself is never parsed: an accepted,
    // complete answer is the success signal, and judging its content would fail a working model on a quibble.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonElement ProbeSchema =
        StructuredChatRequest.CreateStrictSchema(typeof(ProbeReply), JsonOptions);

    // providerKey null = probe whatever the resolution chain picks (Ai:DefaultProvider / sole provider);
    // a key probes that specific provider, so a new one can be verified before the default is flipped to it.
    public async Task<TestAiConnectionResponse> HandleAsync(string? providerKey, CancellationToken ct)
    {
        // Gate first: an unconfigured app or an unavailable provider (missing or misconfigured — the
        // resolver never substitutes) reports a friendly result rather than throwing from GetChatClient.
        var provider = providerResolver.Resolve(providerKey);
        if (provider is null)
            return new TestAiConnectionResponse(false, "AI is not configured.", null);
        if (!provider.IsConfigured)
            return new TestAiConnectionResponse(
                false, $"AI provider '{provider.Key}' is unavailable: {provider.ConfigurationError}", null);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProbeTimeout);

        var sw = Stopwatch.StartNew();
        try
        {
            // The provider owns this client for the app lifetime — do NOT dispose it. Built inside the try
            // as defense in depth: validation pre-checks the endpoint URI now, but a build failure should
            // still surface as Success=false rather than escaping as an unhandled 500.
            var client = provider.GetChatClient();

            // SendAsync sets no MaxOutputTokens, matching the production paths: Azure reasoning deployments
            // (observed with a real gpt-5.4) reject a cap the reasoning burn + reply can't fit in with an HTTP 400
            // instead of truncating, so ANY fixed cap here could misreport a working deployment as broken. The
            // model stops after the one-field reply, and ProbeTimeout bounds a genuinely runaway response.
            // (An Anthropic provider still sends a cap — the Claude API requires one: this buffered probe gets
            // AnthropicChatOptionsClient.NonStreamingMaxOutputTokens, as classification and payee resolution do.)
            // Buffered rather than streamed: the reply is a few tokens, well inside the buffered path's timeout.
            // The message says "JSON" on purpose: with StructuredOutput=false the pipeline downgrades this to
            // json_object mode, which OpenAI and Azure OpenAI refuse (400) unless a message contains that word —
            // the services' prompts all do, so without it the probe would fail a config the real calls accept.
            await StructuredChatRequest.SendAsync(
                client,
                [new ChatMessage(ChatRole.User, """Reply with the JSON object {"ok": true}.""")],
                ProbeSchema,
                "connection_test",
                "Confirms the model can answer with structured output.",
                stream: false,
                "AI connection test",
                cts.Token);

            return new TestAiConnectionResponse(true, "Connection successful.", (int)sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller (HTTP request) was cancelled — honor it rather than reporting a fake failure.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Our own timeout fired — report it as a friendly connection result.
            return new TestAiConnectionResponse(false, "Connection timed out.", null);
        }
        catch (Exception ex)
        {
            // A provider SDK error (a 400 naming a rejected option, a 401, an unreachable host) reports the
            // provider's own message; anything else — a declined or cut-off reply (AiResponseIncompleteException),
            // a client-build failure — reports its own.
            var message = AiProviderErrors.TryGetStatus(ex, out _, out var providerMessage)
                && !string.IsNullOrWhiteSpace(providerMessage)
                    ? providerMessage
                    : ex.Message;
            return new TestAiConnectionResponse(false, message, null);
        }
    }

    private sealed class ProbeReply
    {
        [Description("Always true.")]
        public bool Ok { get; set; }
    }
}
