using System.Diagnostics.Metrics;

namespace Homestead.WebApi.Infrastructure.Ai;

// Token and request metrics for the Aspire dashboard (the SDK emits spans but no metrics). The authoritative credit
// view is the Anthropic Console; this is the at-a-glance one. Tags are the feature, the model that served the reply
// and an outcome — never a document name or any content.
public sealed class AiTelemetry : IDisposable
{
    public const string MeterName = Microsoft.Extensions.Hosting.Extensions.HomesteadAiMeterName;

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _tokens;
    private readonly Counter<long> _requests;
    private readonly Histogram<double> _duration;

    public AiTelemetry()
    {
        _tokens = _meter.CreateCounter<long>("homestead.ai.tokens", "{token}",
            "Tokens billed per AI call, by kind (input, output, thinking, cache_read, cache_creation).");
        _requests = _meter.CreateCounter<long>("homestead.ai.requests", "{request}",
            "AI calls by outcome (ok, fallback, incomplete, error).");
        _duration = _meter.CreateHistogram<double>("homestead.ai.duration", "s", "Wall-clock duration of an AI call.");
    }

    // A reply arrived (complete or not — refused and cut-off replies are billed too, so they are counted).
    // thinking is a breakdown of output, not an addition to it; it is recorded under its own kind for visibility.
    public void RecordReply(AiFeature feature, string model, AiUsage usage, string outcome, TimeSpan duration)
    {
        var featureTag = FeatureTag(feature);
        RecordTokens(featureTag, model, "input", usage.InputTokens);
        RecordTokens(featureTag, model, "output", usage.OutputTokens);
        RecordTokens(featureTag, model, "thinking", usage.ThinkingTokens);
        RecordTokens(featureTag, model, "cache_read", usage.CacheReadInputTokens);
        RecordTokens(featureTag, model, "cache_creation", usage.CacheCreationInputTokens);
        RecordRequest(featureTag, model, outcome, duration);
    }

    // No reply at all (an HTTP error, a cut-off stream, a network failure).
    public void RecordError(AiFeature feature, string model, TimeSpan duration) =>
        RecordRequest(FeatureTag(feature), model, Outcomes.Error, duration);

    public static class Outcomes
    {
        public const string Ok = "ok";
        public const string Fallback = "fallback";
        public const string Incomplete = "incomplete";
        public const string Error = "error";
    }

    private void RecordTokens(string feature, string model, string kind, long count)
    {
        if (count > 0)
            _tokens.Add(count, new("feature", feature), new("model", model), new("kind", kind));
    }

    private void RecordRequest(string feature, string model, string outcome, TimeSpan duration)
    {
        _requests.Add(1, new("feature", feature), new("model", model), new("outcome", outcome));
        _duration.Record(duration.TotalSeconds, new("feature", feature), new("model", model), new("outcome", outcome));
    }

    public static string FeatureTag(AiFeature feature) => feature switch
    {
        AiFeature.Classification => "classification",
        AiFeature.StatementExtraction => "statement_extraction",
        AiFeature.PayeeResolution => "payee_resolution",
        AiFeature.ConnectionProbe => "connection_probe",
        _ => feature.ToString(),
    };

    public void Dispose() => _meter.Dispose();
}
