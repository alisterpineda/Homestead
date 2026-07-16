using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Stand-in <see cref="IAiProvider"/> that reports itself configured, so the resolved-provider gates in
/// <c>DocumentClassifier</c> / <c>ParseAccountStatementJobHandler</c> open without any live endpoint. The
/// real chat client is never built — classify/summarize/statement flows run through
/// <see cref="FakeAiClassificationService"/> and <see cref="FakeStatementExtractionService"/> — so
/// <see cref="GetChatClient"/> throws if it is ever reached. Knobs are the production defaults.
/// </summary>
public sealed class FakeAiProvider : IAiProvider
{
    // Also pinned as Ai:DefaultProvider by HomesteadApiFactory so this fake resolves explicitly,
    // regardless of any real providers leaking in from environment config.
    public const string ProviderKey = "fake-ai";

    public string Key => ProviderKey;
    public AiProviderType? Type => AiProviderType.OpenAiCompatible;
    public bool IsConfigured => true;
    public string? ConfigurationError => null;

    public string? Endpoint => "http://fake-ai.test/v1";
    public string? Model => "fake-model";
    public bool HasApiKey => false;

    public int MaxInputChars => AiProviderOptions.DefaultMaxInputChars;
    public int StatementMaxInputChars => AiProviderOptions.DefaultStatementMaxInputChars;
    public bool ImageInput => true;
    public int ImageRenderMaxEdge => AiProviderOptions.DefaultImageRenderMaxEdge;

    public IChatClient GetChatClient() =>
        throw new NotSupportedException("FakeAiProvider.GetChatClient must not be called in tests.");
}
