using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.DeletePriceSourceSchedule;

public class DeletePriceSourceScheduleHandler(AppDbContext db, IPriceSourceScheduleStore scheduleStore)
    : IRequestHandler<DeletePriceSourceScheduleCommand, bool>
{
    public async Task<bool> Handle(DeletePriceSourceScheduleCommand request, CancellationToken ct)
    {
        request.Source.RequireExternal("scheduled");

        var removed = await scheduleStore.RemoveBySourceAsync(request.Source, ct);
        if (!removed)
            throw new EntityNotFoundException("Price source schedule not found.");

        await db.SaveChangesAsync(ct);
        return true;
    }
}
