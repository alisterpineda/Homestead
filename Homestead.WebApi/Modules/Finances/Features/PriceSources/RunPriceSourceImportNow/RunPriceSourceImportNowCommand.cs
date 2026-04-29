using Homestead.WebApi.Domain.Finances;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.RunPriceSourceImportNow;

public record RunPriceSourceImportNowCommand : IRequest<RunPriceSourceImportNowResponse>
{
    public CommodityPriceSource Source { get; init; }
    public DateTimeOffset? FromAt { get; init; }
    public DateTimeOffset? ToAt { get; init; }
    public IReadOnlyList<Guid>? CommodityIds { get; init; }
}

public record RunPriceSourceImportNowResponse(Guid JobId);
