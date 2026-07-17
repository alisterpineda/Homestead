namespace Homestead.WebApi.Domain.Finances;

public class NormalTransaction : AccountTransaction
{
    /// <summary>
    /// Discriminates the user-facing shape of this normal transaction (Transfer / Buy / Sell /
    /// Dividend). Stored as a string column, nullable in the shared TPH table (NULL on OB/BE rows),
    /// but always set in code for normal transactions. Defaults to <see cref="NormalShape.Transfer"/>.
    /// </summary>
    public NormalShape NormalShape { get; set; }

    /// <summary>
    /// Optional counterparty for this flow (Expense/Income). Nullable TPH column on the shared
    /// transaction table — NULL on transfers/trades/dividends and on OB/BE rows. No nav property;
    /// payee names are batch-loaded via <c>PayeeNames</c>.
    /// </summary>
    public Guid? PayeeId { get; set; }
}
