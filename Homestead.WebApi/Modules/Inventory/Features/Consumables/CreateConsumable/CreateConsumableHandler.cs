using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.CreateConsumable;

public class CreateConsumableHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateConsumableCommand, ConsumableResponse>
{
    public async Task<ConsumableResponse> Handle(CreateConsumableCommand request, CancellationToken ct)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Fungibility = Fungibility.Fungible,
            Status = ItemStatus.Active,
            Unit = request.Unit.Trim(),
            RestockThreshold = request.RestockThreshold,
            Notes = Normalize(request.Notes),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = currentUser.UserId,
            UpdatedByUserId = currentUser.UserId,
        };

        db.Items.Add(item);
        audit.Created("Item", item.Id);

        await unitOfWork.SaveChangesAsync(ct);

        return await db.Items
            .AsNoTracking()
            .Where(i => i.Id == item.Id)
            .Select(ConsumableResponseMapper.Projection)
            .FirstAsync(ct);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
