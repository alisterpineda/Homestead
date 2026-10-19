using System.Security.Cryptography;
using System.Text.Json;

namespace Homestead.WebApi.Infrastructure.Ai;

// Provider-neutral request/reply records. Content parts are ours, never SDK types, so a consumer never learns
// which backend answered.

// Tags the request for metrics. Never part of what the model is sent.
public enum AiFeature { Classification, StatementExtraction, PayeeResolution, ConnectionProbe }

// Anthropic's effort scale (how much thinking a reply gets); another backend maps to its nearest equivalent.
public enum AiEffort { Low, Medium, High, XHigh, Max }

public sealed record AiRequest(
    AiFeature Feature,
    // Always set by the caller from AiInputSettings, so a change of default never moves a workload silently.
    AiEffort Effort,
    string SystemPrompt,
    // The provider orders document/image parts before text, whatever order they arrive in.
    IReadOnlyList<AiContentPart> UserContent,
    // A strict JSON schema (AiJsonSchema.CreateStrict, or hand-built) the reply is constrained to. Descriptions the
    // model should see go on the schema's properties: the Claude API's output format carries the schema alone.
    JsonElement Schema,
    // The reply may be long (statement line items): the provider streams and raises its output cap.
    bool LongOutput,
    // Names the request in exception messages ONLY ("Classification of document 'x'") — never a metric tag or a
    // log field, since it carries a document name.
    string RequestLabel);

public abstract record AiContentPart;

public sealed record AiTextPart(string Text) : AiContentPart;

// The byte parts print a size and hash instead of their bytes, so a logged record never dumps a document.
public sealed record AiImagePart(byte[] Bytes, string MediaType) : AiContentPart
{
    public override string ToString() => $"({MediaType}, {AiContentDigest.Describe(Bytes)})";
}

public sealed record AiPdfPart(byte[] Bytes) : AiContentPart
{
    public override string ToString() => $"(application/pdf, {AiContentDigest.Describe(Bytes)})";
}

public static class AiContentDigest
{
    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string Describe(byte[] bytes) => $"{bytes.Length} bytes, sha256:{Sha256(bytes)}";
}

public sealed record AiUsage(
    long InputTokens, long OutputTokens, long ThinkingTokens, long CacheReadInputTokens, long CacheCreationInputTokens)
{
    public static readonly AiUsage None = new(0, 0, 0, 0, 0);
}

// Model is the model that SERVED the reply — after a refusal fallback it differs from the configured one.
public sealed record AiReply(string Json, string Model, AiUsage Usage);
