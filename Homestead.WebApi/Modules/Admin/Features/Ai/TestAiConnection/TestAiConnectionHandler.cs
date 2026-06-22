using System.Diagnostics;
using Homestead.WebApi.Infrastructure.Ai;
using MediatR;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;

// Live probe of the single static AI config: sends a one-token "ping" and reports reachability.
// Writes nothing — see TestAiConnectionCommand for why this is IRequest, not ICommand.
public class TestAiConnectionHandler(IAiClientFactory clientFactory)
    : IRequestHandler<TestAiConnectionCommand, TestAiConnectionResponse>
{
    // Generous on purpose: the real classification path (AiClassificationService) passes the job's token
    // straight to GetResponseAsync with no cap, so a slow-but-working self-hosted model can take far longer
    // than a few seconds for even a 1-token reply. A short probe cap would report "timed out" (reads as
    // broken) on a model that classification actually drives fine. 60s still bounds a genuinely hung endpoint.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(60);

    public async Task<TestAiConnectionResponse> Handle(TestAiConnectionCommand command, CancellationToken ct)
    {
        // Gate first: an unconfigured app reports a friendly result rather than throwing from CreateClient.
        if (!clientFactory.IsConfigured)
            return new TestAiConnectionResponse(false, "AI is not configured.", null);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProbeTimeout);

        var sw = Stopwatch.StartNew();
        try
        {
            // The factory owns this client for the app lifetime — do NOT dispose it. Build it inside the try
            // so a build/config failure (e.g. a malformed Ai:Endpoint, which still passes IsConfigured)
            // surfaces as Success=false rather than escaping as an unhandled 500.
            var client = clientFactory.CreateClient();

            await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "ping")],
                new ChatOptions { MaxOutputTokens = 1 },
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
