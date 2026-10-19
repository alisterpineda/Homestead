using Homestead.WebApi.Infrastructure.Ai;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

// Shared scaffolding for suites that consume AI (classification, statements, admin probes) and care about what a
// handler does with the provider rather than about the provider itself. Mirrors TestTextExtraction's role for the
// OCR engine registry.
public static class TestAiProviders
{
    // The production defaults, optionally with knobs overridden — the record literal every consumer is handed.
    public static AiInputSettings Settings(
        AiEffort effort = AiEffort.Medium, int? maxInputChars = null, int? statementMaxInputChars = null,
        bool imageInput = true, int? imageRenderMaxEdge = null,
        bool pdfInput = true, int? pdfInputMaxPages = null, long? pdfInputMaxBytes = null) =>
        AiInputSettings.Default with
        {
            Effort = effort,
            MaxInputChars = maxInputChars ?? AiInputSettings.Default.MaxInputChars,
            StatementMaxInputChars = statementMaxInputChars ?? AiInputSettings.Default.StatementMaxInputChars,
            ImageInput = imageInput,
            ImageRenderMaxEdge = imageRenderMaxEdge ?? AiInputSettings.Default.ImageRenderMaxEdge,
            PdfInput = pdfInput,
            PdfInputMaxPages = pdfInputMaxPages ?? AiInputSettings.Default.PdfInputMaxPages,
            PdfInputMaxBytes = pdfInputMaxBytes ?? AiInputSettings.Default.PdfInputMaxBytes,
        };

    // A ready provider whose SendAsync answers with the given JSON (served by "test-model"). Tests that need a
    // different answer reconfigure SendAsync themselves.
    public static IAiProvider Provider(string replyJson = "{}")
    {
        var provider = Substitute.For<IAiProvider>();
        provider.Availability.Returns(AiAvailability.Ready);
        provider.SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>())
            .Returns(Reply(replyJson));
        return provider;
    }

    public static AiReply Reply(string json) => new(json, "test-model", AiUsage.None);

    // Config that is there but cannot run; SendAsync throws like the real one.
    public static IAiProvider Unconfigured(string reason = "Ai:ApiKey is required.") =>
        WithAvailability(AiAvailability.Unavailable(reason), $"AI is unavailable: {reason}");

    // No Ai:* config at all.
    public static IAiProvider Absent() => WithAvailability(AiAvailability.Absent, "AI is not configured.");

    // Records every request the provider is sent; the returned function reads the latest (null before any).
    public static IAiProvider Capture(this IAiProvider provider, out Func<AiRequest?> lastRequest)
    {
        AiRequest? captured = null;
        provider.SendAsync(Arg.Do<AiRequest>(r => captured = r), Arg.Any<CancellationToken>());
        lastRequest = () => captured;
        return provider;
    }

    // The concatenated text parts of a request — what the services' "user message" assertions read.
    public static string UserText(this AiRequest request) =>
        string.Concat(request.UserContent.OfType<AiTextPart>().Select(p => p.Text));

    private static IAiProvider WithAvailability(AiAvailability availability, string message)
    {
        var provider = Substitute.For<IAiProvider>();
        provider.Availability.Returns(availability);
        provider.SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>())
            .Returns<AiReply>(_ => throw new InvalidOperationException(message));
        return provider;
    }
}
