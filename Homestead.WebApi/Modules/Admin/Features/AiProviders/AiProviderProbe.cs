using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders;

// Shared resolution for the two "probe" slices (test-connection, list-models), which both accept
// either an existing provider id (merge the masked/blank secret with the stored config) or an
// ad-hoc {providerType, configuration} body.
public static class AiProviderProbe
{
    // Upper bound on a single live provider round-trip so a stalled/unreachable endpoint can't pin
    // a request thread for the OpenAI SDK's ~100s default network timeout.
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static async Task<(AiProviderType ProviderType, string? Configuration)> ResolveAsync(
        AppDbContext db, Guid? id, AiProviderType? providerType, string? configuration, CancellationToken ct)
    {
        if (id is { } providerId)
        {
            var provider = await db.AiProviders.AsNoTracking().FirstOrDefaultAsync(e => e.Id == providerId, ct)
                ?? throw new EntityNotFoundException("AI provider not found.");

            // A blank/masked key in the incoming config reuses the stored secret; a null body
            // means "use exactly what's stored".
            var merged = configuration is null
                ? provider.Configuration
                : JsonSecretConfig.Merge(configuration, provider.Configuration, AiProviderSecrets.Keys);

            return (provider.ProviderType, merged);
        }

        if (providerType is not { } pt)
            throw new BusinessRuleException("Provider type is required.");

        return (pt, configuration);
    }
}
