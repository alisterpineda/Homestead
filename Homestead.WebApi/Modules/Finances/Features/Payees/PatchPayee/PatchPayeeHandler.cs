using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.PatchPayee;

public class PatchPayeeHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork,
    IValidator<PatchPayeeCommand> validator)
{
    public async Task<PayeeResponse> HandleAsync(PatchPayeeCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var payee = await db.Payees.FirstOrDefaultAsync(p => p.Id == request.Id, ct)
            ?? throw new EntityNotFoundException("Payee not found.");

        // Snapshot before mutating so the diff captures the original values.
        audit.Track<PayeeSnapshot, Payee>(payee, "Payee", payee.Id);

        var name = request.Name.Trim();

        Account? defaultCategory = null;
        if (request.DefaultCategoryId is Guid categoryId)
            defaultCategory = await PayeeWriteHelpers.ValidateDefaultCategoryAsync(db, categoryId, ct);

        if (await PayeeWriteHelpers.NameTakenAsync(db, name, request.Id, ct))
            throw new DuplicateEntityException($"A payee named '{name}' already exists.");

        payee.Name = name;
        payee.DefaultCategoryId = request.DefaultCategoryId;
        payee.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        payee.IsArchived = request.IsArchived;
        payee.UpdatedAt = DateTimeOffset.UtcNow;
        payee.UpdatedByUserId = currentUser.UserId;

        await unitOfWork.SaveChangesAsync(ct);

        var transactionCount = await db.AccountTransactions.OfType<NormalTransaction>()
            .CountAsync(t => t.PayeeId == payee.Id, ct);
        return PayeeResponseMapper.ToResponse(payee, transactionCount, defaultCategory);
    }
}
