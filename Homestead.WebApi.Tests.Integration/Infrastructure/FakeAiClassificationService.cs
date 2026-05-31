using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Documents.Services;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Deterministic stand-in for the real AI service so integration tests can exercise the
/// classify/summarize flows without a live model. Returns only a canned summary (gated by scope)
/// and an unchanged title — no tag/party matches, keeping results FK-safe regardless of the DB.
/// Only reached when a provider resolves (an enabled provider is seeded + its id is passed).
/// </summary>
public sealed class FakeAiClassificationService : IAiClassificationService
{
    public const string GeneratedSummary = "AI generated summary.";

    // Optional gate so a test can hold a classify/summarize call mid-flight and inspect server state (used
    // by the idle-in-transaction acceptance test). Static because the service is registered scoped (new
    // instance per request scope) and the whole integration suite runs serially, so only one test arms the
    // gate at a time. Default: null = no gating, so every other test is unaffected. Always release/reset in
    // a finally.
    private static TaskCompletionSource? _entered;
    private static TaskCompletionSource? _release;

    /// <summary>
    /// Arms the gate. The returned <c>Entered</c> task completes when a classify call reaches the gate;
    /// calling <c>Release</c> lets that call proceed. Reset with <see cref="ResetGate"/> in a finally.
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

    public async Task<ClassificationResult?> ClassifyAsync(
        AiProvider provider,
        string documentName,
        string? extractedText,
        List<TagInfo> availableTags,
        List<PartyInfo> availableParties,
        ClassificationScope scope,
        CancellationToken ct)
    {
        // Snapshot the gate up front so a concurrent ResetGate() can't tear it out mid-call.
        var entered = _entered;
        var release = _release;
        if (release is not null)
        {
            entered?.TrySetResult();
            await release.Task.WaitAsync(ct);
        }

        var summary = scope.HasFlag(ClassificationScope.Summary) ? GeneratedSummary : string.Empty;
        return new ClassificationResult([], [], summary, documentName);
    }
}
