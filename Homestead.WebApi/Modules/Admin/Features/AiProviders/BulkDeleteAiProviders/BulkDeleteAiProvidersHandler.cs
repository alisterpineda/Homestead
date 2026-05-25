using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.BulkDeleteAiProviders;

public class BulkDeleteAiProvidersHandler(AppDbContext db, IAuditContext audit)
    : IRequestHandler<BulkDeleteAiProvidersCommand, BulkDeleteAiProvidersResponse>
{
    public async Task<BulkDeleteAiProvidersResponse> Handle(BulkDeleteAiProvidersCommand request, CancellationToken ct)
    {
        if (request.Ids.Count == 0)
            return new BulkDeleteAiProvidersResponse(0);

        var providers = await db.AiProviders
            .Where(e => request.Ids.Contains(e.Id))
            .ToListAsync(ct);

        if (providers.Count == 0)
            return new BulkDeleteAiProvidersResponse(0);

        foreach (var provider in providers)
            audit.Deleted("AiProvider", provider.Id);

        db.AiProviders.RemoveRange(providers);
        await db.SaveChangesAsync(ct);

        return new BulkDeleteAiProvidersResponse(providers.Count);
    }
}
