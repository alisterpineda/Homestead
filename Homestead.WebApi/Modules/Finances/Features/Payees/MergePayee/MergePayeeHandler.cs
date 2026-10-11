using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.MergePayee;

/// <summary>
/// Consolidate a source payee into a target: re-point every transaction and every learned mapping from
/// source to target, then delete the source. The cleanup valve for AI-created near-duplicates
/// ("Blue Bottle" / "Blue Bottle Coffee") that a case-insensitive name check can't catch.
/// </summary>
public class MergePayeeHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
{
    public async Task<PayeeResponse> HandleAsync(Guid id, MergePayeeRequest body, CancellationToken ct)
    {
        if (id == body.TargetId)
            throw new BusinessRuleException("A payee cannot be merged into itself.");

        var source = await db.Payees.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new EntityNotFoundException("Source payee not found.");

        var target = await db.Payees.FirstOrDefaultAsync(p => p.Id == body.TargetId, ct)
            ?? throw new EntityNotFoundException("Target payee not found.");

        // Re-point every flow attributed to the source onto the target (MergeBrand precedent). The bulk
        // ExecuteUpdate over the TPH OfType<> filter shares the command transaction automatically with the
        // tracked Payees.Remove + flush below, so the re-point and the deletion are atomic without a manual
        // tx. The per-transaction PayeeId change is not field-audited; the payee deletion is.
        await db.AccountTransactions.OfType<NormalTransaction>()
            .Where(t => t.PayeeId == source.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.PayeeId, target.Id), ct);

        // Re-point learned mappings BEFORE removing the source: the PayeeId FK is Cascade, so a mapping still
        // pointing at the source would be deleted with it. NormalizedKey is globally unique, so re-pointing
        // never collides on the target.
        await db.PayeeMappings
            .Where(m => m.PayeeId == source.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.PayeeId, target.Id), ct);

        audit.Deleted("Payee", source.Id, source.Name);
        db.Payees.Remove(source);

        await unitOfWork.SaveChangesAsync(ct);

        var targetCategory = target.DefaultCategoryId is Guid categoryId
            ? await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == categoryId, ct)
            : null;

        var transactionCount = await db.AccountTransactions.OfType<NormalTransaction>()
            .CountAsync(t => t.PayeeId == target.Id, ct);

        return PayeeResponseMapper.ToResponse(target, transactionCount, targetCategory);
    }
}
