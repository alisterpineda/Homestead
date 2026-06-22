using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Stand-in for <see cref="IAiClientFactory"/> that reports AI as statically configured, so
/// <c>DocumentClassifier</c>'s IsConfigured gate opens without any live endpoint. The real chat client is
/// never built — classify/summarize run through <see cref="FakeAiClassificationService"/> — so
/// <see cref="CreateClient"/> throws if it is ever reached.
/// </summary>
public sealed class FakeAiClientFactory : IAiClientFactory
{
    public bool IsConfigured => true;

    public IChatClient CreateClient() =>
        throw new NotSupportedException("FakeAiClientFactory.CreateClient must not be called in tests.");
}
