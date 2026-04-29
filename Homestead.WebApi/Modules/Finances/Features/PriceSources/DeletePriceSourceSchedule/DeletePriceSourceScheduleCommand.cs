using Homestead.WebApi.Domain.Finances;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.DeletePriceSourceSchedule;

public record DeletePriceSourceScheduleCommand(CommodityPriceSource Source) : IRequest<bool>;
