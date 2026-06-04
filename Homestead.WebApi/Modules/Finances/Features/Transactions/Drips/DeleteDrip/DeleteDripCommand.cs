using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.DeleteDrip;

public record DeleteDripCommand(Guid TransactionId) : ICommand<Unit>;
