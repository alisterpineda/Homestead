using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;

// Live probe of the configured model: sends a tiny structured-output request through IAiProvider — the same
// strict-schema request, effort and refusal-fallback settings every AI service sends — and reports whether the
// model accepted and answered it. More than reachability: a wrong key, an unknown model ID or an effort the model
// doesn't take fails here, at setup time, with the API's own message shown on /admin/ai instead of failing every
// real job later.
//
// Deliberately write-free: the endpoint is a POST, so the command transaction filter wraps it, but the
// transaction opens lazily on the first flush with work. A write here would open it and pin a pooled
// connection for the duration of the slow external call (the idle-in-transaction shape the lazy-tx
// work fights).
public class TestAiConnectionHandler(IAiProvider aiProvider, AiInputSettings settings)
{
    // Generous on purpose: the real classification path passes the job's token straight through with no time
    // cap, and with thinking always on even a one-field reply can take a while. A short probe cap would report
    // "timed out" (reads as broken) on a model classification drives fine. 60s still bounds a hung call.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(60);

    // Generated like the services' schemas — from the reply DTO, with the options a reply would be read with —
    // so the probe exercises AiJsonSchema.CreateStrict's shape too. The reply itself is never parsed: an accepted,
    // complete answer is the success signal, and judging its content would fail a working model on a quibble.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonElement ProbeSchema = AiJsonSchema.CreateStrict(typeof(ProbeReply), JsonOptions);

    public async Task<TestAiConnectionResponse> HandleAsync(CancellationToken ct)
    {
        // Gate first: absent or misconfigured AI reports a friendly result rather than throwing from SendAsync.
        var availability = aiProvider.Availability;
        if (!availability.IsPresent)
            return new TestAiConnectionResponse(false, "AI is not configured.", null);
        if (!availability.IsConfigured)
            return new TestAiConnectionResponse(false, $"AI is unavailable: {availability.Reason}", null);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProbeTimeout);

        var sw = Stopwatch.StartNew();
        try
        {
            // Buffered (the reply is a few tokens) at the configured effort, like the real calls.
            await aiProvider.SendAsync(new AiRequest(
                AiFeature.ConnectionProbe, settings.Effort,
                "You are a connection test. Answer exactly as asked.",
                [new AiTextPart("""Reply with {"ok": true}.""")],
                ProbeSchema,
                LongOutput: false,
                "AI connection test"), cts.Token);

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
            // An API error (a 400 naming a rejected option, a 401, an unreachable host) reports the API's own
            // message; anything else — a declined or cut-off reply (AiResponseIncompleteException) — reports its own.
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
