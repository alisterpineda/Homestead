using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
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

        return StatementImportResponse.FromJob(job, accountId, staged);
    }
}
