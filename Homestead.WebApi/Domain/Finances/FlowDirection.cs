namespace Homestead.WebApi.Domain.Finances;

/// <summary>
/// Direction of an ordinary spend/income flow. <see cref="Expense"/> posts money out of the user
/// account (and into the system Expense account); <see cref="Income"/> posts money in (out of the
/// system Income account). Carried in the create request body; immutable once a flow is written
/// (fixed by the stored <see cref="NormalShape"/>).
/// </summary>
public enum FlowDirection
{
    Expense,
    Income,
}
