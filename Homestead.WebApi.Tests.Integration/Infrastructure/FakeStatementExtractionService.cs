using Homestead.WebApi.Modules.Finances.Processing.Statements;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Deterministic stand-in for <see cref="IStatementExtractionService"/> so the import spine runs without a
/// live model. Returns a fixed bank-statement header (masked account hint <c>••••1234</c>, closing balance
/// 1500 on 2026-06-30) — enough to exercise the mapping proposal, mis-binding guard, and commit path.
/// </summary>
public sealed class FakeStatementExtractionService : IStatementExtractionService
{
    public const string MaskedHint = "••••1234";
    public const decimal ClosingBalance = 1500m;
    public static readonly DateOnly PeriodEnd = new(2026, 6, 30);

    public Task<StatementIr> ExtractAsync(string extractedText, byte[]? pageImage, CancellationToken ct)
    {
        var ir = new StatementIr
        {
            Kind = StatementKind.Bank,
            Header = new StatementHeaderIr
            {
                Institution = "Test Bank",
                AccountNumberMasked = MaskedHint,
                Currency = "CAD",
                PeriodStart = new DateOnly(2026, 6, 1),
                PeriodEnd = PeriodEnd,
                OpeningBalance = new MoneyIr(1000m, "Previous Balance $1,000.00"),
                ClosingBalance = new MoneyIr(ClosingBalance, "New Balance $1,500.00"),
            },
        };
        return Task.FromResult(ir);
    }
}
