namespace Homestead.WebApi.Infrastructure.Ai;

// The exit door: the one seam between Homestead and the model behind it. One registered singleton, no resolver.
// Pure I/O plus availability — the consumer-side input policy (text budgets, image settings) is AiInputSettings,
// identical whichever backend runs, so an implementation, the integration fake and the eval decorator each stay
// at two members. Another backend is a second implementation selected by config; prompts, schemas and reply DTOs
// are plain data and move over unchanged.
public interface IAiProvider
{
    AiAvailability Availability { get; }

    // One structured-output request, one attempt. Throws InvalidOperationException (naming the reason) when
    // unconfigured, AiResponseIncompleteException when the reply did not finish cleanly, and the backend SDK's own
    // exceptions otherwise (AiProviderErrors reads their status).
    Task<AiReply> SendAsync(AiRequest request, CancellationToken ct);
}

// Absent config, misconfigured config and "ready" are three states callers tell apart: the statement job and the
// probe word their messages differently for the first two (the E2E suite pins the absent-case text). Reason is
// set only when IsPresent && !IsConfigured, and is what the admin status page shows.
public sealed record AiAvailability(bool IsConfigured, bool IsPresent, string? Reason)
{
    public static readonly AiAvailability Absent = new(false, false, null);
    public static readonly AiAvailability Ready = new(true, true, null);

    public static AiAvailability Unavailable(string reason) => new(false, true, reason);

    // The one wording of "can't send": what SendAsync throws, and what a caller that must not proceed throws.
    public void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException(IsPresent ? $"AI is unavailable: {Reason}" : "AI is not configured.");
    }
}
