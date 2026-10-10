namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccount;

public record GetAccountQuery
{
    public Guid Id { get; init; }

    /// <summary>
    /// Cutoff for transaction inclusion and price resolution. Defaults to end-of-day
    /// UTC today when null. Frontend sends a local-end-of-day offset so user "today"
    /// is the source of truth.
    /// </summary>
    public DateTimeOffset? AsOf { get; init; }
}
