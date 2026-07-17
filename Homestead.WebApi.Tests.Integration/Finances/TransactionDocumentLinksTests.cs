using System.Net;
using System.Net.Http.Json;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class TransactionDocumentLinksTests : IntegrationTestBase
{
    public TransactionDocumentLinksTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> GetCurrencyIdAsync(string symbol)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == symbol).Select(c => c.Id).SingleAsync(CT);
    }

    private async Task<Guid> CreateCashAccountAsync(HttpClient client, Guid currencyId)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Chequing-Links",
            kind = "Cash",
            reportingCurrencyId = currencyId,
            initialBalance = new { amount = 0m, asOfDate = "2026-01-01" },
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        return body!.Id;
    }

    private async Task<Guid> CreateFlowAsync(HttpClient client, Guid accountId)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Expense",
            accountId,
            amount = 42.10m,
            date = "2026-06-03",
            description = "GROCERY MART",
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<TransactionItem>(CT);
        return body!.Id;
    }

    private async Task UntrashAsync(Guid documentId)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Documents.IgnoreQueryFilters([DocumentQueryFilters.SoftDelete])
            .Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.DeletedAt, (DateTimeOffset?)null)
                .SetProperty(d => d.DeletedByUserId, (Guid?)null), CT);
    }

    [Fact]
    public async Task ManualLinks_FullSpine()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var accountId = await CreateCashAccountAsync(client, cad);
        var txId = await CreateFlowAsync(client, accountId);
        var docA = await UploadAndWaitAsync(client, "receipt-a.pdf");
        var docB = await UploadAndWaitAsync(client, "receipt-b.pdf");

        // 1. Fresh transaction: no links, count 0.
        var links = await GetLinksAsync(client, txId);
        Assert.Empty(links);
        var tx = await client.GetFromJsonAsync<TransactionItem>(
            $"/api/v1/finances/transactions/{txId}", CT);
        Assert.Equal(0, tx!.DocumentLinkCount);

        // 2. Link docA → 201 with a fully resolved Manual/Available response.
        var addResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/transactions/{txId}/documents", new { documentId = docA }, CT);
        Assert.Equal(HttpStatusCode.Created, addResp.StatusCode);
        var added = await addResp.Content.ReadFromJsonAsync<LinkItem>(CT);
        Assert.Equal(docA, added!.DocumentId);
        Assert.NotNull(added.DocumentName);
        Assert.Equal("Manual", added.Origin);
        Assert.Equal("Available", added.Availability);

        // 3. Duplicate link → 409; unknown transaction / document → 404.
        var dupResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/transactions/{txId}/documents", new { documentId = docA }, CT);
        Assert.Equal(HttpStatusCode.Conflict, dupResp.StatusCode);

        var unknownTxResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/transactions/{Guid.NewGuid()}/documents", new { documentId = docA }, CT);
        Assert.Equal(HttpStatusCode.NotFound, unknownTxResp.StatusCode);

        var unknownDocResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/transactions/{txId}/documents", new { documentId = Guid.NewGuid() }, CT);
        Assert.Equal(HttpStatusCode.NotFound, unknownDocResp.StatusCode);

        // 4. The account activity list surfaces the count without joining Documents.
        var page = await client.GetFromJsonAsync<TransactionPage>(
            $"/api/v1/finances/accounts/{accountId}/transactions?page=1&pageSize=50", CT);
        var row = Assert.Single(page!.Items, t => t.Id == txId);
        Assert.Equal(1, row.DocumentLinkCount);

        // 5. A trashed document cannot be newly linked (404, same guard as import initiate)...
        var trashB = await client.DeleteAsync($"/api/v1/documents/{docB}", CT);
        trashB.EnsureSuccessStatusCode();
        var trashedAddResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/transactions/{txId}/documents", new { documentId = docB }, CT);
        Assert.Equal(HttpStatusCode.NotFound, trashedAddResp.StatusCode);

        // ...but restoring it makes it linkable.
        await UntrashAsync(docB);
        var addBResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/transactions/{txId}/documents", new { documentId = docB }, CT);
        Assert.Equal(HttpStatusCode.Created, addBResp.StatusCode);

        // 6. Trashing an already-linked document keeps the link, rendered as Trashed with its name.
        var trashA = await client.DeleteAsync($"/api/v1/documents/{docA}", CT);
        trashA.EnsureSuccessStatusCode();
        links = await GetLinksAsync(client, txId);
        Assert.Equal(2, links.Count);
        var linkA = Assert.Single(links, l => l.DocumentId == docA);
        Assert.Equal("Trashed", linkA.Availability);
        Assert.NotNull(linkA.DocumentName);
        Assert.Equal("Available", Assert.Single(links, l => l.DocumentId == docB).Availability);

        // 7. Removing a link works even while its document is in the trash.
        var removeResp = await client.DeleteAsync(
            $"/api/v1/finances/transactions/{txId}/documents/{docA}", CT);
        Assert.Equal(HttpStatusCode.NoContent, removeResp.StatusCode);
        links = await GetLinksAsync(client, txId);
        Assert.Equal(docB, Assert.Single(links).DocumentId);

        // 8. Import-origin links cannot be removed individually (rollback owns them).
        var tx2 = await CreateFlowAsync(client, accountId);
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TransactionDocumentLinks.Add(new TransactionDocumentLink
            {
                AccountTransactionId = tx2,
                DocumentId = docB,
                Origin = TransactionDocumentLinkOrigin.StatementImport,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(CT);
        }
        var removeImportResp = await client.DeleteAsync(
            $"/api/v1/finances/transactions/{tx2}/documents/{docB}", CT);
        Assert.Equal(HttpStatusCode.BadRequest, removeImportResp.StatusCode);

        // 9. Permanent delete cascades away every link to the document (manual + import-origin).
        (await client.DeleteAsync($"/api/v1/documents/{docB}", CT)).EnsureSuccessStatusCode();
        var purgeResp = await client.PostAsync($"/api/v1/documents/{docB}/permanently-delete", null, CT);
        purgeResp.EnsureSuccessStatusCode();
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.TransactionDocumentLinks.AnyAsync(l => l.DocumentId == docB, CT));
        }
        Assert.Empty(await GetLinksAsync(client, txId));
    }

    private async Task<IReadOnlyList<LinkItem>> GetLinksAsync(HttpClient client, Guid transactionId)
    {
        var resp = await client.GetAsync($"/api/v1/finances/transactions/{transactionId}/documents", CT);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<LinkItem>>(CT))!;
    }

    private record TransactionItem(Guid Id, int DocumentLinkCount);
    private record TransactionPage(IReadOnlyList<TransactionItem> Items);
    private record LinkItem(
        Guid DocumentId, string? DocumentName, string Origin, string Availability, DateTimeOffset CreatedAt);
}
