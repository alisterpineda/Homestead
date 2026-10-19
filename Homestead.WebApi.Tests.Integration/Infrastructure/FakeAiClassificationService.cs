using Homestead.WebApi.Modules.Documents.Services;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Deterministic stand-in for the real AI service so integration tests can exercise the
/// classify/summarize flows without a live model. Returns only a canned summary (gated by scope)
/// and an unchanged title — no tag/party matches, keeping results FK-safe regardless of the DB.
/// Reached whenever classify/summarize runs, since <see cref="FakeAiProvider"/> reports AI configured.
/// </summary>
public sealed class FakeAiClassificationService : IAiClassificationService
{
    public const string GeneratedSummary = "AI generated summary.";

    // Optional gate so a test can hold a summarize call mid-flight and inspect server state (used by the
    // idle-in-transaction acceptance test). Static because the service is registered scoped (new instance
    // per request scope) and the whole integration suite runs serially, so only one test arms the gate at
    // a time. The gate fires ONLY on the Summary scope (see ClassifyAsync), so a background classify job
    // can't trip it during the armed window. Default: null = no gating, so every other test is unaffected.
    // Always release/reset in a finally.
    private static TaskCompletionSource? _entered;
    private static TaskCompletionSource? _release;

    /// <summary>
    /// Arms the gate. The returned <c>Entered</c> task completes when a summarize call (a classification
    /// carrying the <see cref="ClassificationScope.Summary"/> flag) reaches the gate; calling <c>Release</c>
    /// lets that call proceed. Reset with <see cref="ResetGate"/> in a finally.
    /// </summary>
    public static (Task Entered, Action Release) ArmGate()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _entered = entered;
        _release = release;
        return (entered.Task, () => release.TrySetResult());
    }

    public static void ResetGate()
    {
        _entered = null;
        _release = null;
    }

    // Captures the page image and PDF of the most recent classify call so input-mode tests can assert what the
    // classifier received. Static for the same reason as the gate (scoped service, serial suite); reset via
    // ResetLastCall in a finally when a test inspects it.
    public static byte[]? LastPageImage { get; private set; }
    public static byte[]? LastPdf { get; private set; }
    public static ClassificationScope? LastScope { get; private set; }

    public static void ResetLastCall()
    {
        LastPageImage = null;
        LastPdf = null;
        LastScope = null;
    }

    public async Task<ClassificationResult?> ClassifyAsync(
        ClassificationInput input,
        List<TagInfo> availableTags,
        List<PartyInfo> availableParties,
        ClassificationScope scope,
        CancellationToken ct)
    {
        LastPageImage = input.PageImage;
        LastPdf = input.Pdf;
        LastScope = scope;

        // Gate ONLY the dedicated summarize path (the one the idle-in-transaction acceptance test arms
        // around). Both summarize entry points pass EXACTLY ClassificationScope.Summary, so an exact-equality
        // check excludes every other path that might resolve a provider during the armed window: classify/
        // bulk-classify (FromFields → Tags|Parties|Name, never Summary) AND upload auto-classify
        // (ClassificationScope.All — which would satisfy HasFlag(Summary) but not equality). That keeps a
        // background job from entering the gate from the wrong call and snapshotting pg_stat_activity at the
        // wrong moment. (The summary-return below still uses HasFlag so composite scopes like All keep
        // getting a summary — only the gate needs to be this strict.)
        if (scope == ClassificationScope.Summary)
        {
            // Snapshot the gate up front so a concurrent ResetGate() can't tear it out mid-call.
            var entered = _entered;
            var release = _release;
            if (release is not null)
            {
                entered?.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        }

        var summary = scope.HasFlag(ClassificationScope.Summary) ? GeneratedSummary : string.Empty;
        return new ClassificationResult([], [], summary, input.DocumentName);
    }
}
