using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

/// <summary>
/// The date-range guard on the document list query. Exercised through <see cref="GetDocumentsHandler"/> itself:
/// the guard throws before any query is built, so EF InMemory never has to translate the handler's
/// Postgres-only FTS/trigram functions.
/// </summary>
public class GetDocumentsDateRangeGuardTests : IDisposable
{
    private readonly TestAppDbContext _db = TestAppDbContext.Create();
    private readonly IDocumentQueryService _accessService = Substitute.For<IDocumentQueryService>();

    public void Dispose() => _db.Dispose();

    private GetDocumentsHandler CreateSut() => new(_db, _accessService);

    [Fact]
    public async Task HandleAsync_DocumentDateFromAfterTo_ThrowsBusinessRule()
    {
        var query = new GetDocumentsQuery
        {
            DocumentDateFrom = new DateOnly(2024, 6, 2),
            DocumentDateTo = new DateOnly(2024, 6, 1)
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => CreateSut().HandleAsync(query, TestContext.Current.CancellationToken));

        Assert.Contains("DocumentDateFrom", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_ExpiryDateFromAfterTo_ThrowsBusinessRule()
    {
        var query = new GetDocumentsQuery
        {
            ExpiryDateFrom = new DateOnly(2030, 1, 1),
            ExpiryDateTo = new DateOnly(2029, 12, 31)
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => CreateSut().HandleAsync(query, TestContext.Current.CancellationToken));

        Assert.Contains("ExpiryDateFrom", ex.Message);
    }
}
