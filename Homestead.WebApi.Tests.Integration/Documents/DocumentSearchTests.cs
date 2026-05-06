using System.Text.RegularExpressions;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;


namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class DocumentSearchTests : IntegrationTestBase
{
    public DocumentSearchTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetDocuments_SearchByName_FindsMatch()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "UniqueInvoice.pdf");
        await UploadAndWaitAsync(client, "OtherContract.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=UniqueInvoice", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Name == "UniqueInvoice.pdf");
    }

    [Fact]
    public async Task GetDocuments_SearchNoMatch_ReturnsEmpty()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "RealDocument.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=xyznonexistent", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Empty(body!.Items);
    }

    [Fact]
    public async Task GetDocuments_FilterByTag_ReturnsTagged()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var tagResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "FilterTag" }, CT);
        tagResponse.EnsureSuccessStatusCode();
        var tag = await tagResponse.Content.ReadFromJsonAsync<TagResult>(CT);

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/tagPaths", value = (object)new[] { "FilterTag" } }
        });
        patchResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&TagIds={tag!.Id}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Id == docId);
    }

    [Fact]
    public async Task GetDocuments_FilterByParty_ReturnsLinked()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var partyResponse = await client.PostAsJsonAsync("/api/v1/documents/parties", new { name = "FilterParty" }, CT);
        partyResponse.EnsureSuccessStatusCode();
        var party = await partyResponse.Content.ReadFromJsonAsync<PartyResult>(CT);

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/partyIds", value = (object)new[] { party!.Id } }
        });
        patchResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&PartyIds={party.Id}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Id == docId);
    }

    [Fact]
    public async Task GetDocuments_SortByName_OrdersCorrectly()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Bravo.pdf");
        await UploadAndWaitAsync(client, "Alpha.pdf");
        await UploadAndWaitAsync(client, "Charlie.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&SortBy=Name&SortDir=Asc", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        var names = body!.Items.Select(d => d.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
    }

    [Fact]
    public async Task GetDocuments_Pagination_RespectsPageSize()
    {
        var client = await CreateAdminClientAsync();
        for (var i = 0; i < 5; i++)
            await UploadAndWaitAsync(client, $"Page{i}.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=2&IncludeChildTags=false", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Equal(2, body!.Items.Count);
        Assert.Equal(5, body.TotalCount);
    }

    [Fact]
    public async Task GetDocuments_PrefixSearch_MatchesStemmedWord()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Consultation Agreement.pdf");
        await UploadAndWaitAsync(client, "Unrelated Report.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=consult", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Name == "Consultation Agreement.pdf");
    }

    [Fact]
    public async Task GetDocuments_ShortPrefix_MatchesViaILike()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Consultation Agreement.pdf");
        await UploadAndWaitAsync(client, "Unrelated Report.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=consu", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Name == "Consultation Agreement.pdf");
    }

    [Fact]
    public async Task GetDocuments_MultiWordSearch_MatchesBothTerms()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Tax Invoice 2024.pdf");
        await UploadAndWaitAsync(client, "Tax Return 2024.pdf");
        await UploadAndWaitAsync(client, "Sales Invoice 2024.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=tax+invoice", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Name == "Tax Invoice 2024.pdf");
    }

    [Fact]
    public async Task GetDocuments_SpecialCharactersInSearch_DoesNotThrow()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Normal Document.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=test%26%26%7C%7C!%27%3A*", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetDocuments_DefaultSort_OrdersByCreatedAtDesc()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "First.pdf");
        await UploadAndWaitAsync(client, "Second.pdf");
        await UploadAndWaitAsync(client, "Third.pdf");

        // No SortBy/SortDir params — should default to CreatedAt Desc (newest first)
        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        var names = body!.Items.Select(d => d.Name).ToList();
        Assert.Equal(["Third.pdf", "Second.pdf", "First.pdf"], names);
    }

    [Fact]
    public async Task GetDocuments_SortByCreatedAtAsc_OrdersOldestFirst()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "First.pdf");
        await UploadAndWaitAsync(client, "Second.pdf");
        await UploadAndWaitAsync(client, "Third.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&SortBy=CreatedAt&SortDir=Asc", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        var names = body!.Items.Select(d => d.Name).ToList();
        Assert.Equal(["First.pdf", "Second.pdf", "Third.pdf"], names);
    }

    [Fact]
    public async Task GetDocuments_PhraseSearch_MatchesAdjacentWords()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Operating Agreement 2024.pdf");
        await UploadAndWaitAsync(client, "Operating Manual.pdf");
        await UploadAndWaitAsync(client, "Service Agreement.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=%22operating%20agreement%22", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Name == "Operating Agreement 2024.pdf");
    }

    [Fact]
    public async Task GetDocuments_PhraseSearch_DoesNotMatchSeparatedWords()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Agreement for Operating Lease.pdf");
        await UploadAndWaitAsync(client, "Operating Agreement.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=%22operating%20agreement%22", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Name == "Operating Agreement.pdf");
    }

    [Fact]
    public async Task GetDocuments_MixedPhraseAndPrefix_RequiresBoth()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Operating Agreement 2024.pdf");
        await UploadAndWaitAsync(client, "Operating Agreement 2023.pdf");
        await UploadAndWaitAsync(client, "Tax Invoice 2024.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=%22operating%20agreement%22%202024", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Name == "Operating Agreement 2024.pdf");
    }

    [Fact]
    public async Task GetDocuments_UnmatchedQuote_TreatedAsPrefixSearch()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Quarterly Report.pdf");
        await UploadAndWaitAsync(client, "Unrelated Memo.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=quarterly%22report", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Name == "Quarterly Report.pdf");
    }

    [Fact]
    public async Task GetDocuments_PhraseWithSpecialChars_DoesNotThrow()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Normal Document.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=%22foo%26%26%7Cbar%22%3A*", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetDocuments_EmptyQuotes_FallsBackToNoSearch()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Some Document.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=%22%22", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetDocuments_Response_IncludesCreatedAt()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Timestamped.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        var doc = Assert.Single(body!.Items);
        Assert.True(doc.CreatedAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task GetDocuments_SearchByTagName_FindsMatch()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Doc1.pdf");

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/tagPaths", value = (object)new[] { "Invoice" } }
        });
        patchResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=invoice", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Id == docId);
    }

    [Fact]
    public async Task GetDocuments_SearchByTagName_CaseInsensitive()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Doc1.pdf");

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/tagPaths", value = (object)new[] { "Invoice" } }
        });
        patchResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=INVOICE", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Id == docId);
    }

    [Fact]
    public async Task GetDocuments_SearchByTagName_Substring()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Doc1.pdf");

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/tagPaths", value = (object)new[] { "Invoice" } }
        });
        patchResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=invoi", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Id == docId);
    }

    [Fact]
    public async Task GetDocuments_SearchByPartyName_FindsMatch()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Doc1.pdf");

        var partyResponse = await client.PostAsJsonAsync("/api/v1/documents/parties", new { name = "Acme Corp" }, CT);
        partyResponse.EnsureSuccessStatusCode();
        var party = await partyResponse.Content.ReadFromJsonAsync<PartyResult>(CT);

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/partyIds", value = (object)new[] { party!.Id } }
        });
        patchResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=acme", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Id == docId);
    }

    [Fact]
    public async Task GetDocuments_SearchByPartyName_CaseInsensitive()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Doc1.pdf");

        var partyResponse = await client.PostAsJsonAsync("/api/v1/documents/parties", new { name = "Acme Corp" }, CT);
        partyResponse.EnsureSuccessStatusCode();
        var party = await partyResponse.Content.ReadFromJsonAsync<PartyResult>(CT);

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/partyIds", value = (object)new[] { party!.Id } }
        });
        patchResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=ACME", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Id == docId);
    }

    [Fact]
    public async Task GetDocuments_SearchByTagName_NoMatch_StillProducesSuggestions()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Invoice 2024.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=invioce", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResultWithSuggestions<DocumentListItem>>(CT);
        Assert.Empty(body!.Items);
        Assert.Contains("Invoice 2024.pdf", body.Suggestions);
    }

    [Fact]
    public async Task Snippet_ContentMatch_ReturnsHighlightedExcerpt()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Receipt.pdf");
        await SetExtractedTextAsync(docId, "This receipt records the annual property tax payment for fiscal year 2024.");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=tax", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentSnippetItem>>(CT);
        var doc = Assert.Single(body!.Items, d => d.Id == docId);
        Assert.NotNull(doc.MatchSnippet);
        Assert.Contains("<mark>tax</mark>", doc.MatchSnippet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Snippet_RealFixtureExtraction_ReturnsHighlightedExcerpt()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadFixtureAndWaitAsync(client, "irs-form-w9.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=Taxpayer", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentSnippetItem>>(CT);
        var doc = Assert.Single(body!.Items, d => d.Id == docId);
        Assert.NotNull(doc.MatchSnippet);
        Assert.Contains("<mark>Taxpayer</mark>", doc.MatchSnippet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Snippet_LongFormPdf_ReturnsRelevantSnippet()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadFixtureAndWaitAsync(client, "gutenberg-scrap-book-elementary-math.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=fraction", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentSnippetItem>>(CT);
        var doc = Assert.Single(body!.Items, d => d.Id == docId);
        Assert.NotNull(doc.MatchSnippet);
        Assert.Contains("<mark>", doc.MatchSnippet);
    }

    [Fact]
    public async Task Search_NoisyOcrPdf_FindsExtractedToken()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadFixtureAndWaitAsync(client, "usda-farmers-bulletin-1923-index.pdf");

        string? extractedText;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            extractedText = await db.DocumentVersions
                .AsNoTracking()
                .Where(v => v.DocumentId == docId)
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => v.ExtractedText)
                .FirstAsync(CT);
        }

        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        var token = Regex.Match(extractedText!, @"\b[A-Za-z]{6,}\b").Value;
        Assert.NotEmpty(token);

        var response = await client.GetAsync(
            $"/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search={Uri.EscapeDataString(token)}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentSnippetItem>>(CT);
        Assert.Contains(body!.Items, d => d.Id == docId);
    }

    [Fact]
    public async Task Snippet_NameOnlyMatch_ReturnsNull()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "TaxReceipt.pdf");
        await SetExtractedTextAsync(docId, "Lorem ipsum dolor sit amet, no relevant words here.");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=taxreceipt", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentSnippetItem>>(CT);
        var doc = Assert.Single(body!.Items, d => d.Id == docId);
        Assert.Null(doc.MatchSnippet);
    }

    [Fact]
    public async Task Snippet_TagOrPartyOnlyMatch_ReturnsNull()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Doc1.pdf");
        await SetExtractedTextAsync(docId, "Lorem ipsum dolor sit amet, no relevant words here.");

        var partyResponse = await client.PostAsJsonAsync("/api/v1/documents/parties", new { name = "Globex Industries" }, CT);
        partyResponse.EnsureSuccessStatusCode();
        var party = await partyResponse.Content.ReadFromJsonAsync<PartyResult>(CT);

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/partyIds", value = (object)new[] { party!.Id } }
        });
        patchResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=globex", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentSnippetItem>>(CT);
        var doc = Assert.Single(body!.Items, d => d.Id == docId);
        Assert.Null(doc.MatchSnippet);
    }

    [Fact]
    public async Task Snippet_NullExtractedText_ReturnsNull()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "TaxNoText.pdf");
        await SetExtractedTextAsync(docId, null);

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=tax", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentSnippetItem>>(CT);
        var doc = Assert.Single(body!.Items, d => d.Id == docId);
        Assert.Null(doc.MatchSnippet);
    }

    [Fact]
    public async Task Snippet_HtmlSafe_EscapesAngleBracketsAndAmpersands()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Hostile.pdf");
        // Use raw `<`, `>`, `&` outside tag-like patterns so PG's text-search
        // parser keeps them in the headline output and the sanitizer must
        // escape them.
        await SetExtractedTextAsync(docId,
            "Outstanding tax balance: 5 > 1 & penalties < interest accrued for the year.");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=tax", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentSnippetItem>>(CT);
        var doc = Assert.Single(body!.Items, d => d.Id == docId);
        Assert.NotNull(doc.MatchSnippet);
        Assert.Contains("<mark>tax</mark>", doc.MatchSnippet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&gt;", doc.MatchSnippet);
        Assert.Contains("&lt;", doc.MatchSnippet);
        Assert.Contains("&amp;", doc.MatchSnippet);
        // Raw, unencoded markup chars (other than our injected <mark> tags) must not survive.
        var withoutMark = doc.MatchSnippet!
            .Replace("<mark>", string.Empty)
            .Replace("</mark>", string.Empty);
        Assert.DoesNotContain("<", withoutMark);
        Assert.DoesNotContain(">", withoutMark);
    }

    [Fact]
    public async Task Snippet_NoSearch_ReturnsNull()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Receipt.pdf");
        await SetExtractedTextAsync(docId, "Annual tax payment summary for fiscal year 2024.");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentSnippetItem>>(CT);
        Assert.All(body!.Items, item => Assert.Null(item.MatchSnippet));
    }

    private async Task SetExtractedTextAsync(Guid documentId, string? extractedText)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var version = await db.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstAsync(CT);
        version.ExtractedText = extractedText;
        await db.SaveChangesAsync(CT);

        var updater = scope.ServiceProvider.GetRequiredService<ISearchVectorUpdater>();
        await updater.UpdateSearchVectorAsync(documentId, CT);
    }

    private record DocumentListItem(Guid Id, string Name, string ContentType, long FileSize, string FileExtension, DateTimeOffset CreatedAt);
    private record DocumentSnippetItem(Guid Id, string Name, string? MatchSnippet);
    private record TagResult(Guid Id, string Name, string Path);
    private record PartyResult(Guid Id, string Name);
}
