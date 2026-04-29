using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Processing;

public record ImportCommodityPricesPayload(
    CommodityPriceSource Source,
    DateTimeOffset? FromAt = null,
    DateTimeOffset? ToAt = null,
    IReadOnlyList<Guid>? CommodityIds = null);
