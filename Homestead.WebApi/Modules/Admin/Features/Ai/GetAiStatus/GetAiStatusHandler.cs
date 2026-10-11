using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Modules.Admin.Features.Ai.GetAiStatus;

// Pure read off the static provider registrations — no DB access. A query, not a command, so it never
// opens the UnitOfWork transaction.
public class GetAiStatusHandler(IEnumerable<IAiProvider> providers, IAiProviderResolver resolver)
{
    public Task<List<AiProviderStatusResponse>> HandleAsync(CancellationToken ct)
    {
        // For a DEFINED default the resolver returns the canonical registered instance (the same
        // singletons DI enumerates here), so default-ness is reference identity — no string re-matching
        // that could drift from the resolver's own key semantics. A DANGLING Ai:DefaultProvider resolves
        // to a fresh synthetic instance that is never in the list.
        var defaultProvider = resolver.Resolve();

        var response = providers
            .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => ToResponse(p, IsDefault: ReferenceEquals(p, defaultProvider)))
            .ToList();

        // Append the dangling default as its own row (unavailable, marked default) rather than leaving the
        // list with no default marked — the admin page is where an operator debugs exactly this typo.
        if (defaultProvider is not null && !providers.Contains(defaultProvider))
            response.Add(ToResponse(defaultProvider, IsDefault: true));

        return Task.FromResult(response);
    }

    private static AiProviderStatusResponse ToResponse(IAiProvider p, bool IsDefault) => new(
        Key: p.Key,
        Type: p.Type,
        IsConfigured: p.IsConfigured,
        ConfigurationError: p.ConfigurationError,
        IsDefault: IsDefault,
        Endpoint: p.Endpoint,
        Model: p.Model,
        HasApiKey: p.HasApiKey,
        MaxInputChars: p.MaxInputChars);
}
