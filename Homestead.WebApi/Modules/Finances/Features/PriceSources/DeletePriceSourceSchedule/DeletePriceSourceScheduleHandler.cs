using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.DeletePriceSourceSchedule;

public class DeletePriceSourceScheduleHandler(
    IPriceSourceScheduleStore scheduleStore, IUnitOfWork unitOfWork)
    : IRequestHandler<DeletePriceSourceScheduleCommand, bool>
{
    public async Task<bool> Handle(DeletePriceSourceScheduleCommand request, CancellationToken ct)
    {
        request.Source.RequireExternal("scheduled");

        var removed = await scheduleStore.RemoveBySourceAsync(request.Source, ct);
        if (!removed)
            throw new EntityNotFoundException("Price source schedule not found.");

        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}
