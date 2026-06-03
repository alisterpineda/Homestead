namespace Homestead.WebApi.Domain.Finances;

public class NormalTransaction : AccountTransaction
{
    /// <summary>
    /// Discriminates the user-facing shape of this normal transaction (Transfer / Buy / Sell /
    /// Dividend). Stored as a string column, nullable in the shared TPH table (NULL on OB/BE rows),
    /// but always set in code for normal transactions. Defaults to <see cref="NormalShape.Transfer"/>.
    /// </summary>
    public NormalShape NormalShape { get; set; }
}
