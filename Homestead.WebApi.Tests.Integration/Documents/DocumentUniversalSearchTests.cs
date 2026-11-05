using System.Globalization;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Homestead.WebApi.Tests.Integration.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Documents;

// Universal-search ("⌘K") coverage for the Documents source. The SearchResponseDto / SearchGroupDto /
// SearchHitDto contract records are shared with Inventory/SearchEndpointTests.cs (same GET /api/v1/search
// endpoint). Most cases inject extracted text directly via SetExtractedTextAsync, so they don't need the
// tesseract binary.
[Collection("Integration")]
public class DocumentUniversalSearchTests : IntegrationTestBase
{
    public DocumentUniversalSearchTests(PostgresFixture fixture) : base(fixture) { }

    // ----- Helpers -----

    private static async Task<SearchResponseDto> SearchAsync(HttpClient client, string q, int? limit = null)
    {
        var url = $"/api/v1/search?q={Uri.EscapeDataString(q)}";
        if (limit is { } l)
            url += $"&limit={l}";

        var resp = await client.GetAsync(url, CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<SearchResponseDto>(CT))!;
    }

    private static SearchGroupDto? Documents(SearchResponseDto r) =>
        r.Groups.FirstOrDefault(g => g.Source == "documents");


    private async Task<(DateOnly? DocumentDate, DateTimeOffset CreatedAt)> GetDocumentDatesAsync(Guid documentId)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => new { d.DocumentDate, d.CreatedAt })
            .FirstAsync(CT);
        return (row.DocumentDate, row.CreatedAt);
    }

    // ----- Name match -----

    [Fact]
    public async Task Search_MatchesName_HitInDocumentsGroup()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "UniqueInvoiceDoc.pdf");
        await UploadAndWaitAsync(client, "OtherContract.pdf");

        var group = Documents(await SearchAsync(client, "uniqueinvoicedoc"));

        Assert.NotNull(group);
        var hit = Assert.Single(group!.Results);
        Assert.Equal(docId.ToString(), hit.Id);
        Assert.Equal("UniqueInvoiceDoc.pdf", hit.Title);
    }

    // ----- Subtitle: content snippet vs date fallback -----

    [Fact]
    public async Task Search_ContentMatch_SubtitleIsPlainSnippetWithoutMarkup()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Receipt.pdf");
        await SetExtractedTextAsync(docId, "This receipt records the annual property tax payment for fiscal year 2024.");

        var group = Documents(await SearchAsync(client, "tax"));

        Assert.NotNull(group);
        var hit = Assert.Single(group!.Results, h => h.Id == docId.ToString());
        Assert.NotNull(hit.Subtitle);
        // The snippet is why it matched — it contains the search term…
        Assert.Contains("tax", hit.Subtitle!, StringComparison.OrdinalIgnoreCase);
        // …rendered as plain text: no HTML markup, and no leftover headline sentinels.
        // The list converts the StartSel/StopSel sentinels (STX \u0002 / ETX \u0003) into <mark>
        // tags; the palette strips them entirely, so none of these characters may survive.
        Assert.False(hit.Subtitle!.Contains('\u0002') || hit.Subtitle!.Contains('\u0003'), "subtitle must not contain headline sentinels");
        Assert.DoesNotContain("<", hit.Subtitle!);
        Assert.DoesNotContain(">", hit.Subtitle!);
    }

    [Fact]
    public async Task Search_FoldsAccents_LikeTheListPage()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Factură apă.pdf");
        await UploadAndWaitAsync(client, "Other.pdf");

        var result = await SearchAsync(client, "factura");

        var hit = Assert.Single(Documents(result)!.Results);
        Assert.Equal(docId.ToString(), hit.Id);
    }

    [Fact]
    public async Task Search_NameOnlyMatch_SubtitleIsDateFallback()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "TaxReceipt.pdf");
        // Content has no matching word, so the match is name-only → no snippet → date fallback.
        await SetExtractedTextAsync(docId, "Lorem ipsum dolor sit amet, no relevant words here.");

        var group = Documents(await SearchAsync(client, "taxreceipt"));

        Assert.NotNull(group);
        var hit = Assert.Single(group!.Results, h => h.Id == docId.ToString());

        var (documentDate, createdAt) = await GetDocumentDatesAsync(docId);
        var expected = (documentDate ?? DateOnly.FromDateTime(createdAt.UtcDateTime))
            .ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        Assert.Equal(expected, hit.Subtitle);
    }

    // ----- Tag / party matches -----

    [Fact]
    public async Task Search_MatchesTagName_ReturnsDoc()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "DocWithTag.pdf");

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/tagPaths", value = (object)new[] { "Receipts2099" } }
        });
        patchResponse.EnsureSuccessStatusCode();

        var group = Documents(await SearchAsync(client, "receipts2099"));

        Assert.NotNull(group);
        Assert.Contains(group!.Results, h => h.Id == docId.ToString());
    }

    [Fact]
    public async Task Search_MatchesPartyName_ReturnsDoc()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "DocWithParty.pdf");

        var partyResponse = await client.PostAsJsonAsync("/api/v1/documents/parties", new { name = "Globex Universal Corp" }, CT);
        partyResponse.EnsureSuccessStatusCode();
        var party = await partyResponse.Content.ReadFromJsonAsync<PartyResult>(CT);

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/partyIds", value = (object)new[] { party!.Id } }
        });
        patchResponse.EnsureSuccessStatusCode();

        var group = Documents(await SearchAsync(client, "globex universal"));

        Assert.NotNull(group);
        Assert.Contains(group!.Results, h => h.Id == docId.ToString());
    }

    // ----- Soft-delete exclusion -----

    [Fact]
    public async Task Search_ExcludesSoftDeletedDocument()
    {
        var client = await CreateAdminClientAsync();
        var live = await UploadAndWaitAsync(client, "SoftDelTokenAlpha.pdf");
        var trashed = await UploadAndWaitAsync(client, "SoftDelTokenBeta.pdf");

        await client.DeleteAsync($"/api/v1/documents/{trashed}", CT);

        var group = Documents(await SearchAsync(client, "softdeltoken"));

        Assert.NotNull(group);
        Assert.Contains(group!.Results, h => h.Id == live.ToString());
        Assert.DoesNotContain(group.Results, h => h.Id == trashed.ToString());
    }

    // ----- Capping + total -----

    [Fact]
    public async Task Search_CapsResultsToLimitButTotalReflectsFullCount()
    {
        var client = await CreateAdminClientAsync();
        for (var i = 1; i <= 8; i++)
            await UploadAndWaitAsync(client, $"GizmoDoc {i}.pdf");

        var group = Documents(await SearchAsync(client, "gizmodoc", limit: 3));

        Assert.NotNull(group);
        Assert.Equal(3, group!.Results.Count);
        Assert.Equal(8, group.Total);
    }

    // ----- Type discriminator -----

    [Fact]
    public async Task Search_TypeDiscriminator_ImageVsDocument()
    {
        var client = await CreateAdminClientAsync();
        var imageId = await UploadImageAndWaitAsync(client, "VacationSnapshot.png");
        var pdfId = await UploadAndWaitAsync(client, "VacationContract.pdf");

        var group = Documents(await SearchAsync(client, "vacation"));

        Assert.NotNull(group);
        var imageHit = Assert.Single(group!.Results, h => h.Id == imageId.ToString());
        var pdfHit = Assert.Single(group.Results, h => h.Id == pdfId.ToString());
        Assert.Equal("image", imageHit.Type);
        Assert.Equal("document", pdfHit.Type);
    }

    private record PartyResult(Guid Id, string Name);
}
