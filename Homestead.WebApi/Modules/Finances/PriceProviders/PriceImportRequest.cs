using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.PriceProviders;

public record PriceImportRequest(
    IReadOnlyCollection<Commodity> Commodities,
    DateTimeOffset FromAt,
    DateTimeOffset ToAt,
    string? Config);
