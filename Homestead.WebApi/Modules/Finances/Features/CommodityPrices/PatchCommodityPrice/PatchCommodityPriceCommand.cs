using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.PatchCommodityPrice;

public class PatchCommodityPriceModel
{
    public DateTimeOffset PriceAt { get; set; }
    public decimal Rate { get; set; }
}

public record PatchCommodityPriceCommand : IRequest<CommodityPriceResponse>
{
    public Guid Id { get; init; }
    public required JsonPatchDocument<PatchCommodityPriceModel> PatchDocument { get; init; }
}
