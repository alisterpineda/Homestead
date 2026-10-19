using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Stand-in <see cref="IAiProvider"/> that reports itself ready, so the availability gates in
/// <c>DocumentClassifier</c> / <c>ParseAccountStatementJobHandler</c> open without any live endpoint. No model is
/// ever called — classify/summarize/statement flows run through <see cref="FakeAiClassificationService"/> and
/// <see cref="FakeStatementExtractionService"/> — so <see cref="SendAsync"/> throws if it is ever reached.
/// </summary>
public sealed class FakeAiProvider : IAiProvider
{
    public AiAvailability Availability => AiAvailability.Ready;

    public Task<AiReply> SendAsync(AiRequest request, CancellationToken ct) =>
        throw new NotSupportedException("FakeAiProvider.SendAsync must not be called in tests.");
}
