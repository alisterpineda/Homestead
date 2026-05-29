using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Infrastructure.Ai.Configurations;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.ListAiProviderModels;

public class ListAiProviderModelsHandler(AppDbContext db)
    : IRequestHandler<ListAiProviderModelsCommand, ListModelsResponse>
{
    public async Task<ListModelsResponse> Handle(ListAiProviderModelsCommand command, CancellationToken ct)
    {
        var (providerType, configuration) = await AiProviderProbe.ResolveAsync(
            db, command.Id, command.ProviderType, command.Configuration, ct);

        // Azure inference keys can't enumerate deployments (management-plane ARM creds are required),
        // and Anthropic isn't implemented yet — the UI hides the Fetch button for both.
        if (providerType != AiProviderType.OpenAI)
            return new ListModelsResponse([]);

        // Validate only what listing needs — an API key, and (if supplied) a well-formed endpoint.
        // NOT the default model: the whole point of Fetch-models is to discover a model before one is
        // chosen. Checking the endpoint up front surfaces a friendly message instead of a raw
        // UriFormatException from `new Uri(...)` inside the SDK builder.
        if (!AiProviderConfig.TryParse(configuration, out var config) || config is null
            || string.IsNullOrWhiteSpace(config.ApiKey))
            throw new BusinessRuleException("Could not fetch models: an API key is required.");

        if (!string.IsNullOrWhiteSpace(config.Endpoint)
            && (!Uri.TryCreate(config.Endpoint, UriKind.Absolute, out var endpointUri)
                || (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps)))
            throw new BusinessRuleException("Could not fetch models: the endpoint must be a valid absolute http(s) URL.");

        try
        {
            var openAiClient = OpenAIClientBuilder.Build(config.ApiKey, config.Endpoint, normalizeForAzure: false);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(AiProviderProbe.Timeout);

            var models = (await openAiClient.GetOpenAIModelClient().GetModelsAsync(cts.Token)).Value
                .Select(m => m.Id)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new ListModelsResponse(models);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller (HTTP request) was cancelled — honor it rather than reporting a fetch error.
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new BusinessRuleException("Could not fetch models: the request timed out.");
        }
        catch (Exception ex)
        {
            // The user explicitly clicked Fetch, so surface the reason (unlike test-connection's flag).
            throw new BusinessRuleException($"Could not fetch models: {ex.Message}");
        }
    }
}
