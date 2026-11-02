using Homestead.WebApi.Common;

namespace Homestead.WebApi.Infrastructure.Ai;

// The exit door: the one seam between Homestead and the model behind it. One registered singleton, no resolver.
// Pure I/O plus availability — the consumer-side input policy (text budgets, image settings) is AiInputSettings,
// identical whichever backend runs, so an implementation, the integration fake and the eval decorator each stay
// at two members. Another backend is a second implementation selected by config; prompts, schemas and reply DTOs
// are plain data and move over unchanged.
public interface IAiProvider
{
    // Absent / unavailable-with-reason / ready: the statement job and the probe word their messages differently for
    // the first two (the E2E suite pins the absent-case text); Reason is what the admin status page shows.
    FeatureAvailability Availability { get; }

    // One structured-output request, one attempt. Throws InvalidOperationException (naming the reason) when
    // unconfigured, AiResponseIncompleteException when the reply did not finish cleanly, and the backend SDK's own
    // exceptions otherwise (AiProviderErrors reads their status).
    Task<AiReply> SendAsync(AiRequest request, CancellationToken ct);
}
