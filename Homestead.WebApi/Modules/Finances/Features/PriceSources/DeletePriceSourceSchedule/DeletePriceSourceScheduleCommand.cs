using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.DeletePriceSourceSchedule;

public record DeletePriceSourceScheduleCommand(CommodityPriceSource Source) : ICommand<bool>;
