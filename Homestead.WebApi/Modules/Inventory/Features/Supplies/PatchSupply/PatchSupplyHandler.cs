using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.PatchSupply;

public class PatchSupplyHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchSupplyCommand, SupplyResponse>
{
    // A supply uses only the reduced fungible lifecycle; lending or repairing a count is meaningless.
    private static readonly ItemStatus[] FungibleStatuses = [ItemStatus.Active, ItemStatus.Archived];

    public async Task<SupplyResponse> Handle(PatchSupplyCommand request, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == request.Id, ct);
        if (item is null)
            throw new EntityNotFoundException("Supply not found.");
        if (item.Fungibility != Fungibility.Fungible)
            throw new BusinessRuleException("Only fungible supplies can be edited here.");

        audit.Track<ItemSnapshot, Item>(item, "Item", item.Id);

        var model = new PatchSupplyModel
        {
            Name = item.Name,
            Unit = item.Unit ?? string.Empty,
            RestockThreshold = item.RestockThreshold,
            Notes = item.Notes,
            Status = item.Status,
        };

        request.PatchDocument.ApplyOrThrow(model);

        if (string.IsNullOrWhiteSpace(model.Name))
            throw new BusinessRuleException("Name is required.");
        if (model.Name.Length > 256)
            throw new BusinessRuleException("Name must be 256 characters or fewer.");
        if (string.IsNullOrWhiteSpace(model.Unit))
            throw new BusinessRuleException("Unit is required.");
        if (model.Unit.Length > 32)
            throw new BusinessRuleException("Unit must be 32 characters or fewer.");
        if (model.RestockThreshold is < 0m)
            throw new BusinessRuleException("Restock threshold must be zero or positive.");
        if (!FungibleStatuses.Contains(model.Status))
            throw new BusinessRuleException("A supply can only be Active or Archived.");

        item.Name = model.Name.Trim();
        item.Unit = model.Unit.Trim();
        item.RestockThreshold = model.RestockThreshold;
        item.Notes = Normalize(model.Notes);
        item.Status = model.Status;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.UpdatedByUserId = currentUser.UserId;

        await unitOfWork.SaveChangesAsync(ct);

        return await db.Items
            .AsNoTracking()
            .Where(i => i.Id == item.Id)
            .Select(SupplyResponseMapper.Projection)
            .FirstAsync(ct);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
