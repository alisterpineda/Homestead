using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Modules.Finances.Services;

public interface IPriceSourceScheduleStore
{
    Task<ProcessingSchedule?> FindBySourceAsync(CommodityPriceSource source, CancellationToken ct);
    Task<bool> RemoveBySourceAsync(CommodityPriceSource source, CancellationToken ct);
}
