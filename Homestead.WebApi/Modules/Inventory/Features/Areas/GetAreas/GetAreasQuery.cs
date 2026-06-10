using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.GetAreas;

public record GetAreasQuery : PaginatedRequest, IRequest<PaginatedResponse<AreaResponse>>;
