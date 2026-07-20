using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Modules.Finances.Processing.Statements;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.GetStatementImport;

public class GetStatementImportHandler(AppDbContext db, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetStatementImportQuery, StatementImportResponse>
{
    public async Task<StatementImportResponse> Handle(GetStatementImportQuery request, CancellationToken ct)
    {
        var job = await db.ProcessingJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JobId, ct);

        // Not a statement-import job, or not this user's job → 404 (never leak another user's import).
        if (job is null
            || job.Type != ProcessingJobType.ParseAccountStatement
            || job.CreatedByUserId != currentUser.UserId)
            throw new EntityNotFoundException("Statement import not found.");

        var payload = job.GetPayload<ParseAccountStatementPayload>();
        var accountId = payload?.AccountId ?? Guid.Empty;

        // Staged only once the parse job has run; still-processing jobs return Status with no header.
        var staged = job.GetResult<StagedStatementImport>();

        // Resolve suggested existing-payee (tier ①/②) detail against the live catalog by id — the current
        // name (so a rename between parse and review shows through) and the default category (id + name +
        // type) the client surfaces in the combobox's Suggested group. Tier-③ new-payee names live inline.
        var suggestedPayeeIds = staged?.Proposal.LineItems
            .Where(f => f.SuggestedPayeeId is not null)
            .Select(f => f.SuggestedPayeeId!.Value)
            .Distinct()
            .ToList() ?? [];

        // Project inside the IQueryable so the DefaultCategory navigation is JOINed in SQL. A
        // ToDictionaryAsync(keySelector, elementSelector) instead runs the element selector CLIENT-side on
        // materialized entities, where the un-Included DefaultCategory is null — silently dropping the chip.
        var suggestedPayees = suggestedPayeeIds.Count == 0
            ? new Dictionary<Guid, SuggestedPayeeInfo>()
            : await db.Payees
                .AsNoTracking()
                .Where(p => suggestedPayeeIds.Contains(p.Id))
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.DefaultCategoryId,
                    CategoryName = p.DefaultCategory != null ? p.DefaultCategory.Name : null,
                    CategoryType = p.DefaultCategory != null ? (AccountType?)p.DefaultCategory.AccountType : null,
                })
                .ToDictionaryAsync(
                    x => x.Id,
                    x => new SuggestedPayeeInfo(x.DefaultCategoryId, x.Name, x.CategoryName, x.CategoryType),
                    ct);

        return StatementImportResponse.FromJob(job, accountId, staged, suggestedPayees);
    }
}
