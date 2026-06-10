using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.DeleteItem;

public class DeleteItemHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteItemCommand, bool>
{
    public async Task<bool> Handle(DeleteItemCommand request, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == request.Id, ct);
        if (item is null)
            return false;

        // Delete is the mistake-eraser; an item that has held contents carries history and can only
        // be archived. (The stock-ledger half of this check joins later, with the stock model.)
        var hasChildren = await db.Items.AnyAsync(i => i.ParentItemId == item.Id, ct);
        if (hasChildren)
            throw new BusinessRuleException("Item has contents and cannot be deleted. Archive it instead.");

        db.Items.Remove(item);
        audit.Deleted("Item", item.Id, item.Name);
        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
