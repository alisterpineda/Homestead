using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

// Shared scaffolding for suites that consume AI (classification, statements, admin probes) and care about
// what a handler does with the resolved provider rather than about providers themselves. Mirrors
// TestTextExtraction's role for the OCR engine registry.
public static class TestAiProviders
{
    public const string DefaultKey = "test-ai";

    // A valid OpenAiCompatible bag — the baseline every substitute derives its knob values from.
    public static AiProviderOptions Options() => new()
    {
        Type = AiProviderType.OpenAiCompatible,
        Endpoint = "http://localhost:11434/v1",
        Model = "test-model"
    };

    // A configured provider substitute whose knobs are computed from a real options bag (so floor/default
    // resolution stays honest) and whose GetChatClient returns the given substituted chat client. The knob
    // Returns are lazy over the bag, so a test may mutate the bag after SUT construction (the
    // DocumentClassifier suite's ImageInput toggling idiom) and the provider reflects it.
    public static IAiProvider Provider(
        IChatClient? client = null, AiProviderOptions? options = null, string key = DefaultKey)
    {
        var opts = options ?? Options();
        var provider = Substitute.For<IAiProvider>();
        provider.Key.Returns(key);
        provider.Type.Returns(_ => opts.Type ?? AiProviderType.OpenAiCompatible);
        provider.IsConfigured.Returns(true);
        provider.ConfigurationError.Returns((string?)null);
        provider.Endpoint.Returns(_ => opts.Endpoint);
        provider.Model.Returns(_ => opts.Model);
        provider.HasApiKey.Returns(_ => !string.IsNullOrWhiteSpace(opts.ApiKey));
        provider.MaxInputChars.Returns(_ => opts.ResolveMaxInputChars());
        provider.StatementMaxInputChars.Returns(_ => opts.ResolveStatementMaxInputChars());
        provider.ImageInput.Returns(_ => opts.ImageInput);
        provider.ImageRenderMaxEdge.Returns(_ => opts.ResolveImageRenderMaxEdge());
        provider.GetChatClient().Returns(client ?? Substitute.For<IChatClient>());
        return provider;
    }

    // An unavailable provider — defined as an instance, but not usable. What the resolver hands back for a
    // misconfigured (or, synthetically, undefined) key; GetChatClient throws like the real one.
    public static IAiProvider UnavailableProvider(
        string key = "broken", string error = "Endpoint is required.")
    {
        var provider = Substitute.For<IAiProvider>();
        provider.Key.Returns(key);
        provider.Type.Returns(AiProviderType.OpenAiCompatible);
        provider.IsConfigured.Returns(false);
        provider.ConfigurationError.Returns(error);
        provider.GetChatClient().Returns(_ =>
            throw new InvalidOperationException($"AI provider '{key}' is unavailable: {error}"));
        return provider;
    }

    // A resolver that answers every request (any key, or none) with the given provider.
    public static IAiProviderResolver Resolver(IAiProvider provider)
    {
        var resolver = Substitute.For<IAiProviderResolver>();
        resolver.Resolve(Arg.Any<string?>()).Returns(provider);
        return resolver;
    }

    // The unconfigured state: no provider resolves at all.
    public static IAiProviderResolver Unresolved()
    {
        var resolver = Substitute.For<IAiProviderResolver>();
        resolver.Resolve(Arg.Any<string?>()).Returns((IAiProvider?)null);
        return resolver;
    }
}
