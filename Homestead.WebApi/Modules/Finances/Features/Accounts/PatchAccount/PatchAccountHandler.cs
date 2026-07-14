using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.PatchAccount;

public class PatchAccountHandler(
    AppDbContext db,
    IAuditContext audit,
    ICurrentUserAccessor currentUser,
    IValuationService valuations,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchAccountCommand, AccountResponse>
{
    public async Task<AccountResponse> Handle(PatchAccountCommand request, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.Id, ct);
        if (account is null)
            throw new EntityNotFoundException("Account not found.");

        if (account.IsSystem)
            throw new BusinessRuleException("System accounts cannot be modified.");
        // Categories are mutable only through their own CRUD (/categories). From this endpoint's
        // perspective a category is not an account — 404, consistent with GetAccount hiding it.
        if (!account.AccountType.IsBalanceSheet())
            throw new EntityNotFoundException("Account not found.");

        audit.Track<AccountSnapshot, Account>(account, "Account", account.Id);

        var model = new PatchAccountModel
        {
            Name = account.Name,
            Institution = account.Institution,
            IsArchived = account.IsArchived,
        };

        request.PatchDocument.ApplyOrThrow(model);

        if (string.IsNullOrWhiteSpace(model.Name))
            throw new BusinessRuleException("Name is required.");
        if (model.Name.Length > 256)
            throw new BusinessRuleException("Name must be 256 characters or fewer.");
        if (model.Institution is not null && model.Institution.Length > 128)
            throw new BusinessRuleException("Institution must be 128 characters or fewer.");

        account.Name = model.Name.Trim();
        account.Institution = string.IsNullOrWhiteSpace(model.Institution) ? null : model.Institution.Trim();
        account.IsArchived = model.IsArchived;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        account.UpdatedByUserId = currentUser.UserId;

        await unitOfWork.SaveChangesAsync(ct);

        var ctx = await valuations.LoadContextAsync(ct);
        var valuation = await valuations.ValueAccountAsync(account, ctx, ct);
        var hasTransactions = await db.AccountTransactionSplits
            .AsNoTracking()
            .AnyAsync(s => s.AccountId == account.Id, ct);
        return AccountResponseMapper.ToResponse(account, valuation, hasTransactions);
    }
}
