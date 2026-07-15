using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Processing.Statements;

namespace Homestead.WebApi.Modules.Finances.Features.Imports;

/// <summary>
/// The reviewable state of a statement-import job: its status plus (once parsed) the transcribed header, the
/// proposed closing-balance assertion, and the guard/dedup signals. The full account number is never
/// included — only the server-computed <see cref="AccountNumberMatch"/> verdict.
/// </summary>
public record StatementImportResponse
{
    public required Guid JobId { get; init; }
    public required ProcessingJobStatus Status { get; init; }
    public required Guid AccountId { get; init; }

    public Guid? DocumentId { get; init; }
    public Guid? DocumentVersionId { get; init; }

    public StatementHeaderResponse? Header { get; init; }
    public ProposedBalanceEntryResponse? ProposedBalanceEntry { get; init; }

    public AccountNumberMatch AccountNumberMatch { get; init; } = AccountNumberMatch.Unknown;
    public bool DuplicateAssertionExists { get; init; }

    /// <summary>Populated when <see cref="Status"/> is Failed.</summary>
    public string? Error { get; init; }

    public static StatementImportResponse FromJob(
        ProcessingJob job, Guid accountId, StagedStatementImport? staged)
    {
        if (staged is null)
        {
            return new StatementImportResponse
            {
                JobId = job.Id,
                Status = job.Status,
                AccountId = accountId,
                Error = job.Status == ProcessingJobStatus.Failed ? job.Error : null,
            };
        }

        var header = staged.Ir.Header;
        return new StatementImportResponse
        {
            JobId = job.Id,
            Status = job.Status,
            AccountId = staged.AccountId,
            DocumentId = staged.DocumentId,
            DocumentVersionId = staged.DocumentVersionId,
            Header = new StatementHeaderResponse
            {
                Kind = staged.Ir.Kind,
                Institution = header.Institution,
                AccountNumberMasked = header.AccountNumberMasked,
                Currency = header.Currency,
                PeriodStart = header.PeriodStart,
                PeriodEnd = header.PeriodEnd,
                OpeningBalance = header.OpeningBalance?.Amount,
                OpeningBalanceAnchor = header.OpeningBalance?.SourceAnchor,
                ClosingBalance = header.ClosingBalance?.Amount,
                ClosingBalanceAnchor = header.ClosingBalance?.SourceAnchor,
            },
            ProposedBalanceEntry = staged.Proposal.BalanceEntry is { } be
                ? new ProposedBalanceEntryResponse
                {
                    AssertedAmount = be.AssertedAmount,
                    Date = be.Date,
                    SourceAnchor = be.SourceAnchor,
                    Accepted = be.Accepted,
                }
                : null,
            AccountNumberMatch = staged.Proposal.AccountNumberMatch,
            DuplicateAssertionExists = staged.Proposal.DuplicateAssertionExists,
            Error = job.Status == ProcessingJobStatus.Failed ? job.Error : null,
        };
    }
}

public record StatementHeaderResponse
{
    public required StatementKind Kind { get; init; }
    public string? Institution { get; init; }
    public string? AccountNumberMasked { get; init; }
    public string? Currency { get; init; }
    public DateOnly? PeriodStart { get; init; }
    public DateOnly? PeriodEnd { get; init; }
    public decimal? OpeningBalance { get; init; }
    public string? OpeningBalanceAnchor { get; init; }
    public decimal? ClosingBalance { get; init; }
    public string? ClosingBalanceAnchor { get; init; }
}

public record ProposedBalanceEntryResponse
{
    public required decimal AssertedAmount { get; init; }
    public DateOnly? Date { get; init; }
    public string? SourceAnchor { get; init; }
    public required bool Accepted { get; init; }
}
