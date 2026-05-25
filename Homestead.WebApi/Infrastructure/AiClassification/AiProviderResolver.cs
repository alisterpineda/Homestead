using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Documents.Features.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Infrastructure.AiClassification;

public class AiProviderResolver(AppDbContext db) : IAiProviderResolver
{
    public async Task<AiProvider?> ResolveAsync(Guid? providerId, CancellationToken ct)
    {
        var resolvedId = providerId;

        if (resolvedId is null)
        {
            var entity = await db.AppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == AppSetting.DocumentsModule, ct);

            if (entity is not null)
            {
                var settings = JsonSerializer.Deserialize<DocumentSettings>(entity.Value, FieldChange.JsonOptions);
                resolvedId = settings?.DefaultAiProviderId;
            }
        }

        if (resolvedId is null)
            return null;

        var provider = await db.AiProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == resolvedId && p.IsEnabled, ct);

        return provider;
    }
}
