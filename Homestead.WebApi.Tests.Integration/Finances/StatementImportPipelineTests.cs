using System.Net;
using System.Net.Http.Json;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class StatementImportPipelineTests : IntegrationTestBase
{
    public StatementImportPipelineTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> GetCurrencyIdAsync(string symbol)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == symbol).Select(c => c.Id).SingleAsync(CT);
    }

    private async Task<Guid> CreateCashAccountAsync(HttpClient client, Guid currencyId, string name, string accountNumber)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name,
            kind = "Cash",
            reportingCurrencyId = currencyId,
            accountNumber,
            initialBalance = new { amount = 0m, asOfDate = "2026-01-01" },
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        return body!.Id;
    }

    private async Task<Guid> LatestVersionIdAsync(Guid documentId)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => v.Id)
            .FirstAsync(CT);
    }

    [Fact]
    public async Task ImportSpine_Initiate_Get_Commit_Rollback()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        // Account number's last-4 (1234) matches the fake extractor's masked hint (••••1234) → Match.
        var accountId = await CreateCashAccountAsync(client, cad, "Chequing-Import", "5559991234");

        var documentId = await UploadAndWaitAsync(client, "statement.pdf");
        var versionId = await LatestVersionIdAsync(documentId);

        // 1. Initiate → parse job.
        var initiateResp = await client.PostAsJsonAsync("/api/v1/finances/imports",
            new { accountId, documentVersionId = versionId }, CT);
        Assert.Equal(HttpStatusCode.Created, initiateResp.StatusCode);
        var initiate = await initiateResp.Content.ReadFromJsonAsync<ImportInitiateItem>(CT);
        await WaitForJobCompletionAsync(initiate!.JobId);

        // 2. GET staged review data.
        var getResp = await client.GetAsync($"/api/v1/finances/imports/{initiate.JobId}", CT);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var staged = await getResp.Content.ReadFromJsonAsync<StatementImportItem>(CT);
        Assert.Equal("Completed", staged!.Status);
        Assert.Equal(accountId, staged.AccountId);
        Assert.Equal(versionId, staged.DocumentVersionId);
        Assert.NotNull(staged.ProposedBalanceEntry);
        Assert.Equal(FakeStatementExtractionService.ClosingBalance, staged.ProposedBalanceEntry!.AssertedAmount);
        Assert.Equal(FakeStatementExtractionService.PeriodEnd, staged.ProposedBalanceEntry.Date);
        Assert.Equal("Match", staged.AccountNumberMatch);
        Assert.False(staged.DuplicateAssertionExists);

        // 2b. Review GET surfaces the proposed itemized flows (Feature B).
        Assert.NotNull(staged.LineItems);
        Assert.Equal(2, staged.LineItems!.Count);
        Assert.Equal("Expense", staged.LineItems[0].Direction);
        Assert.True(staged.LineItems[0].Mappable);
        Assert.Equal("Income", staged.LineItems[1].Direction);

        // 3. Commit → balance entry AND two flows created, each stamped with the source version.
        var commitResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/imports/{initiate.JobId}/commit",
            new
            {
                assertedAmount = 1500m,
                date = "2026-06-30",
                memo = "June statement",
                accepted = true,
                lineItems = new[]
                {
                    new { direction = "Expense", amount = 42.10m, date = "2026-06-03", description = "GROCERY MART" },
                    new { direction = "Income", amount = 2000m, date = "2026-06-15", description = "PAYROLL DEPOSIT" },
                },
            }, CT);
        Assert.Equal(HttpStatusCode.OK, commitResp.StatusCode);
        var commit = await commitResp.Content.ReadFromJsonAsync<CommitItem>(CT);
        Assert.True(commit!.Committed);
        Assert.NotNull(commit.EntryId);
        Assert.Equal(2, commit.FlowsCreated);

        // Balance entry + 2 flows all carry the version stamp.
        await AssertSourceStampedAsync(versionId, expectedCount: 3);

        // 4. Re-commit is rejected (already imported this version).
        var recommitResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/imports/{initiate.JobId}/commit",
            new { assertedAmount = 1500m, date = "2026-06-30", memo = (string?)null, accepted = true }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, recommitResp.StatusCode);

        // 5. Rollback removes the whole mixed set (entry + both flows).
        var rollbackResp = await client.DeleteAsync($"/api/v1/finances/imports/{versionId}", CT);
        Assert.Equal(HttpStatusCode.OK, rollbackResp.StatusCode);
        var rollback = await rollbackResp.Content.ReadFromJsonAsync<RollbackItem>(CT);
        Assert.Equal(3, rollback!.DeletedCount);

        await AssertSourceStampedAsync(versionId, expectedCount: 0);
    }

    [Fact]
    public async Task Initiate_VersionWithoutExtractedText_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var accountId = await CreateCashAccountAsync(client, cad, "Chequing-NoText", "5559991234");

        var documentId = await UploadAndWaitAsync(client, "statement.pdf");
        var versionId = await LatestVersionIdAsync(documentId);

        // sample.pdf has a real text layer, so the upload populated ExtractedText. Clearing it reproduces
        // what an upload whose extraction threw leaves behind — those failures are silent by design.
        await SetExtractedTextAsync(documentId, null);

        var resp = await client.PostAsJsonAsync("/api/v1/finances/imports",
            new { accountId, documentVersionId = versionId }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetailsResponse>(CT);
        Assert.Contains("reprocess", problem!.Detail!, StringComparison.OrdinalIgnoreCase);

        // The whole point of gating at enqueue: nothing was queued, so nothing can bill or fail later.
        await AssertNoParseJobAsync();
    }

    [Fact]
    public async Task Initiate_VersionWithWhitespaceOnlyText_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var accountId = await CreateCashAccountAsync(client, cad, "Chequing-Whitespace", "5559991234");

        var documentId = await UploadAndWaitAsync(client, "statement.pdf");
        var versionId = await LatestVersionIdAsync(documentId);

        // ASCII whitespace beyond plain spaces is reachable — Azure returns its content unmodified, so a
        // blank scan yields "\n". The gate's IsNullOrWhiteSpace translates to btrim over " \t\n\r", which
        // covers this input (plain btrim() would strip spaces only). It does NOT cover all of C#'s
        // whitespace — see the NBSP test below for the seam.
        await SetExtractedTextAsync(documentId, " \t\r\n ");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/imports",
            new { accountId, documentVersionId = versionId }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        await AssertNoParseJobAsync();
    }

    [Fact]
    public async Task Initiate_VersionWithNbspOnlyText_PassesGateAndJobFails()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var accountId = await CreateCashAccountAsync(client, cad, "Chequing-Nbsp", "5559991234");

        var documentId = await UploadAndWaitAsync(client, "statement.pdf");
        var versionId = await LatestVersionIdAsync(documentId);

        // NBSP is C# whitespace but sits outside the btrim(" \t\n\r") set the gate's SQL uses, so it passes
        // the gate and fails at the job handler's C# backstop instead. This pins two things: the known seam
        // (documented in ParseAccountStatementJobHandler) stays a conscious choice rather than a surprise,
        // and the projection genuinely translates to SQL — under silent client evaluation C# semantics
        // would apply and this would 400 at initiate, failing the 201 assertion below.
        await SetExtractedTextAsync(documentId, "\u00A0");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/imports",
            new { accountId, documentVersionId = versionId }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var initiate = await resp.Content.ReadFromJsonAsync<ImportInitiateItem>(CT);
        var job = await WaitForJobFailureAsync(initiate!.JobId);
        Assert.Contains("no extracted text", job.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Migration_AddedColumnsAndIndex_Exist()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.True(await ColumnExistsAsync(db, "finances", "account_transactions", "SourceDocumentVersionId"));
        Assert.True(await ColumnExistsAsync(db, "finances", "accounts", "AccountNumber"));
        Assert.True(await ColumnExistsAsync(db, "processing", "jobs", "Result"));

        var indexExists = await db.Database
            .SqlQuery<int>($"""
                SELECT 1 AS "Value" FROM pg_indexes
                WHERE schemaname = 'finances'
                  AND tablename = 'account_transactions'
                  AND indexname = 'IX_account_transactions_SourceDocumentVersionId'
                """)
            .AnyAsync(CT);
        Assert.True(indexExists);
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string schema, string table, string column)
    {
        return await db.Database
            .SqlQuery<int>($"""
                SELECT 1 AS "Value" FROM information_schema.columns
                WHERE table_schema = {schema} AND table_name = {table} AND column_name = {column}
                """)
            .AnyAsync(CT);
    }

    private async Task AssertNoParseJobAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Asserted on Type, not the version id — that lives inside the job's JSON payload, not a column.
        // Respawn resets between tests, and the upload's own rows carry a different Type.
        Assert.False(await db.ProcessingJobs
            .AnyAsync(j => j.Type == ProcessingJobType.ParseAccountStatement, CT));
    }

    private async Task AssertSourceStampedAsync(Guid versionId, int expectedCount)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.AccountTransactions
            .CountAsync(t => t.SourceDocumentVersionId == versionId, CT);
        Assert.Equal(expectedCount, count);
    }

    private record ImportInitiateItem(Guid JobId);
    private record ProposedItem(decimal AssertedAmount, DateOnly? Date, string? SourceAnchor, bool Accepted);
    private record LineItemItem(
        int Index, DateOnly? Date, string? Description, decimal Amount, string Direction,
        decimal? RunningBalance, bool Mappable, bool DuplicateSuspected, bool Accepted);
    private record StatementImportItem(
        Guid JobId, string Status, Guid AccountId, Guid? DocumentId, Guid? DocumentVersionId,
        ProposedItem? ProposedBalanceEntry, IReadOnlyList<LineItemItem>? LineItems,
        string AccountNumberMatch, bool DuplicateAssertionExists, bool TextTruncated, string? Error);
    private record CommitItem(bool Committed, Guid? EntryId, int FlowsCreated, Guid AccountId);
    private record RollbackItem(int DeletedCount);
}
