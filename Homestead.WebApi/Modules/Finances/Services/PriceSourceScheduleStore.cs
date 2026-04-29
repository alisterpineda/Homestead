using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public class PriceSourceScheduleStore(AppDbContext db) : IPriceSourceScheduleStore
{
    public async Task<ProcessingSchedule?> FindBySourceAsync(CommodityPriceSource source, CancellationToken ct)
    {
        var candidates = await db.ProcessingSchedules
            .Where(s => s.JobType == ProcessingJobType.ImportCommodityPrices)
            .ToListAsync(ct);
        foreach (var candidate in candidates)
        {
            var payload = candidate.GetPayload<ImportCommodityPricesPayload>();
            if (payload?.Source == source)
                return candidate;
        }
        return null;
    }

    public async Task<bool> RemoveBySourceAsync(CommodityPriceSource source, CancellationToken ct)
    {
        var existing = await FindBySourceAsync(source, ct);
        if (existing is null) return false;
        db.ProcessingSchedules.Remove(existing);
        return true;
    }
}
