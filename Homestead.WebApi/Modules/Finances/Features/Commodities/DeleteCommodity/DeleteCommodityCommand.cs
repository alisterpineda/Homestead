using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.DeleteCommodity;

public record DeleteCommodityCommand(Guid Id) : ICommand<bool>;
