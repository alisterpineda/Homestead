using System.Diagnostics;
using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;

// Live probe of one configured AI provider: sends a one-token "ping" and reports reachability.
// Writes nothing — see TestAiConnectionCommand for why that matters.
public class TestAiConnectionHandler(IAiProviderResolver providerResolver)
{
    // Generous on purpose: the real classification path (AiClassificationService) passes the job's token
    // straight to GetResponseAsync with no cap, so a slow-but-working self-hosted model can take far longer
    // than a few seconds for even a 1-token reply. A short probe cap would report "timed out" (reads as
    // broken) on a model that classification actually drives fine. 60s still bounds a genuinely hung endpoint.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(60);

    public async Task<TestAiConnectionResponse> Handle(TestAiConnectionCommand command, CancellationToken ct)
    {
        // Gate first: an unconfigured app or an unavailable provider (missing or misconfigured — the
        // resolver never substitutes) reports a friendly result rather than throwing from GetChatClient.
        var provider = providerResolver.Resolve(command.ProviderKey);
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

            // Deliberately uncapped, matching the production paths (classification and statement
            // extraction set no MaxOutputTokens either): Azure reasoning deployments (observed with a real
            // gpt-5.4) reject a cap the reasoning burn + reply can't fit in with an HTTP 400 instead of
            // truncating, so ANY fixed cap can misreport a working deployment as broken. The model stops at
            // EOS after a short answer to "ping", and ProbeTimeout bounds a genuinely runaway response.
            await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "ping")],
                options: null,
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
            return new TestAiConnectionResponse(false, ex.Message, null);
        }
    }
}
