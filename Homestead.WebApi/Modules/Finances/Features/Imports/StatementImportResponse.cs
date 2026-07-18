using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Processing.Statements;

namespace Homestead.WebApi.Modules.Finances.Features.Imports;

/// <summary>
/// The reviewable state of a statement-import job: its status plus (once parsed) the transcribed header, the
/// proposed closing-balance assertion, the proposed itemized flows, and the guard/dedup signals. The full
/// account number is never included — only the server-computed <see cref="AccountNumberMatch"/> verdict.
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

    /// <summary>
    /// Proposed Expense/Income flows (Feature B), in statement order. Empty until the job is parsed or when
    /// the statement had no transaction table. Non-nullable so the generated client always sees an array —
    /// and so ng-openapi-gen's usage scan discovers the item <c>$ref</c> (a nullable-array union hides it).
    /// </summary>
    public IReadOnlyList<StatementLineItemResponse> LineItems { get; init; } = [];

    public AccountNumberMatch AccountNumberMatch { get; init; } = AccountNumberMatch.Unknown;
    public bool DuplicateAssertionExists { get; init; }

    /// <summary>True when the statement text was clipped before extraction — some trailing lines may be missing.</summary>
    public bool TextTruncated { get; init; }

    /// <summary>Populated when <see cref="Status"/> is Failed.</summary>
    public string? Error { get; init; }

    public static StatementImportResponse FromJob(
        ProcessingJob job, Guid accountId, StagedStatementImport? staged,
        IReadOnlyDictionary<Guid, SuggestedPayeeInfo> suggestedPayees)
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
            LineItems = staged.Proposal.LineItems
                .Select(f =>
                {
                    // Existing-payee suggestions (tier ①/②) resolve against the live catalog by id — for the
                    // display name AND the read-only auto-category chip. A new-payee suggestion (tier ③) has
                    // no id and carries its proposed name inline, with no category (the payee doesn't exist yet).
                    var info = f.SuggestedPayeeId is { } pid && suggestedPayees.TryGetValue(pid, out var i)
                        ? i
                        : null;
                    return new StatementLineItemResponse(
                        f.Index, f.Date, f.Description, f.Amount, f.Direction,
                        f.RunningBalance, f.Mappable, f.DuplicateSuspected, f.Accepted,
                        f.SuggestedPayeeId,
                        info?.Name ?? f.SuggestedPayeeName,
                        f.SuggestionSource,
                        info?.DefaultCategoryName,
                        info?.DefaultCategoryType);
                })
                .ToList(),
            AccountNumberMatch = staged.Proposal.AccountNumberMatch,
            DuplicateAssertionExists = staged.Proposal.DuplicateAssertionExists,
            TextTruncated = staged.TextTruncated,
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

/// <summary>
/// A proposed itemized flow surfaced for review. <see cref="Direction"/> is Expense/Income;
/// <see cref="Mappable"/> is false when the line lacked an amount or direction (shown greyed, not
/// committable as-is). The reviewer curates these client-side before the atomic commit.
/// </summary>
public record StatementLineItemResponse(
    int Index,
    DateOnly? Date,
    string? Description,
    decimal Amount,
    FlowDirection Direction,
    decimal? RunningBalance,
    bool Mappable,
    bool DuplicateSuspected,
    bool Accepted,
    // Payee suggestion surfaced for review. SuggestedPayeeId is an existing payee (tiers ①/②); for a
    // new-payee proposal (tier ③) it is null and SuggestedPayeeName carries the proposed name. The client
    // treats an unaccepted Ai* suggestion as a ghost — never auto-applied, never learned.
    Guid? SuggestedPayeeId,
    string? SuggestedPayeeName,
    PayeeSuggestionSource SuggestionSource,
    // The suggested existing payee's default category (name + type), for the read-only auto-category chip.
    // Null for a tier-③ new-payee suggestion or a payee with no default category.
    string? SuggestedPayeeDefaultCategoryName,
    AccountType? SuggestedPayeeDefaultCategoryType);

/// <summary>Resolved detail for a suggested existing payee — its current name and default category.</summary>
public record SuggestedPayeeInfo(string Name, string? DefaultCategoryName, AccountType? DefaultCategoryType);
