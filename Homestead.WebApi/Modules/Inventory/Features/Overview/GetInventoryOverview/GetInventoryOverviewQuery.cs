using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Overview.GetInventoryOverview;

public record GetInventoryOverviewQuery : IRequest<GetInventoryOverviewResponse>;
