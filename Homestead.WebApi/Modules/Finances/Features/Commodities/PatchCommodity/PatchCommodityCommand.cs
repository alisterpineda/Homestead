using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.PatchCommodity;

public class PatchCommodityModel
{
    public string Name { get; set; } = string.Empty;
    public short DisplayPrecision { get; set; }
}

public record PatchCommodityCommand : IRequest<CommodityResponse>
{
    public Guid Id { get; init; }
    public required JsonPatchDocument<PatchCommodityModel> PatchDocument { get; init; }
}
