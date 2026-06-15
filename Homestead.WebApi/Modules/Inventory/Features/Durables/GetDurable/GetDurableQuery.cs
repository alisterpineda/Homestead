using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurable;

public record GetDurableQuery : IRequest<DurableResponse?>
{
    public Guid Id { get; init; }
}
