using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Imports.CommitStatementImport;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.Imports;

public class CommitStatementImportCommandValidatorTests
{
    private readonly CommitStatementImportCommandValidator _sut = new();

    private static CommitStatementImportCommand Command(params CommitLineItem[] lines) => new()
    {
        JobId = Guid.NewGuid(),
        Accepted = false,
        AssertedAmount = 0m,
        Date = new DateOnly(2026, 6, 30),
        LineItems = lines,
    };

    private static CommitLineItem Line(Guid? categoryId) =>
        new(0, FlowDirection.Expense, 10m, new DateOnly(2026, 6, 3), "A", CategoryId: categoryId);

    [Fact]
    public void Validate_NullCategoryId_IsValid()
    {
        var result = _sut.Validate(Command(Line(categoryId: null)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RealCategoryId_IsValid()
    {
        var result = _sut.Validate(Command(Line(categoryId: Guid.NewGuid())));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyGuidCategoryId_IsInvalid()
    {
        // Guid.Empty can never be a real category — the validator rejects it before the handler runs.
        var result = _sut.Validate(Command(Line(categoryId: Guid.Empty)));
        Assert.False(result.IsValid);
    }
}
