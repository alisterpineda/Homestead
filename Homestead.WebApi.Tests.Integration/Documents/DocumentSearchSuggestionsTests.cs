using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

/// <summary>
/// Covers the fuzzy "Did you mean…?" suggestions surfaced when document search returns no results.
/// Access scoping is reused unchanged from the main query, so user-vs-user visibility is not
/// re-verified here — see <c>PartyVisibilityTests</c> / <c>TagVisibilityTests</c> for that coverage.
/// What this file *does* verify, beyond document-name suggestions, is that tag and party names
/// are also suggested, and that a tag/party only surfaces when it is attached to a document
/// inside the caller's scope (otherwise clicking the chip would resolve to zero results).
/// </summary>
[Collection("Integration")]
public class DocumentSearchSuggestionsTests : IntegrationTestBase
{
    public DocumentSearchSuggestionsTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetDocuments_NoResults_ReturnsSimilarNames()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Operating Agreement.pdf");
        await UploadAndWaitAsync(client, "Invoice 2024.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=invioce", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResultWithSuggestions<DocumentListItem>>(CT);
        Assert.Empty(body!.Items);
        Assert.Contains("Invoice 2024.pdf", body.Suggestions);
        Assert.DoesNotContain("Operating Agreement.pdf", body.Suggestions);
    }

    [Fact]
    public async Task GetDocuments_LongName_ShortTypo_ReturnsSuggestion()
    {
        // Regression guard: similarity() penalizes long names heavily, so a 5-char typo
        // against a 22-char filename used to score below the 0.3 threshold even when the
        // intended word ("Mazda") was clearly present. word_similarity() should match the
        // best window inside the name and surface the document.
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Mazda Receipt 2023.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=mazdo", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResultWithSuggestions<DocumentListItem>>(CT);
        Assert.Empty(body!.Items);
        Assert.Contains("Mazda Receipt 2023.pdf", body.Suggestions);
    }

    [Fact]
    public async Task GetDocuments_Suggestions_OrderedByScoreDescending()
    {
        // "mazdo" overlaps strongly with the "Mazda" window but only weakly with "Amazon",
        // so the Mazda doc should rank first.
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Amazon Invoice.pdf");
        await UploadAndWaitAsync(client, "Mazda Receipt.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=mazdo", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResultWithSuggestions<DocumentListItem>>(CT);
        Assert.Empty(body!.Items);
        Assert.Contains("Mazda Receipt.pdf", body.Suggestions);
        var mazdaIndex = body.Suggestions.ToList().IndexOf("Mazda Receipt.pdf");
        var amazonIndex = body.Suggestions.ToList().IndexOf("Amazon Invoice.pdf");
        if (amazonIndex >= 0)
        {
            Assert.True(mazdaIndex < amazonIndex,
                $"Expected 'Mazda Receipt.pdf' to outrank 'Amazon Invoice.pdf'. Got: [{string.Join(", ", body.Suggestions)}]");
        }
    }

    [Fact]
    public async Task GetDocuments_NoResults_BelowThreshold_ReturnsEmpty()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Operating Agreement.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=qqqzzz", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResultWithSuggestions<DocumentListItem>>(CT);
        Assert.Empty(body!.Items);
        Assert.Empty(body.Suggestions);
    }

    [Fact]
    public async Task GetDocuments_ShortSearch_NoSuggestions()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Invoice 2024.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=in", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResultWithSuggestions<DocumentListItem>>(CT);
        Assert.Empty(body!.Suggestions);
    }

    [Fact]
    public async Task GetDocuments_HasResults_NoSuggestions()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Invoice 2024.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=invoice", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResultWithSuggestions<DocumentListItem>>(CT);
        Assert.NotEmpty(body!.Items);
        Assert.Empty(body.Suggestions);
    }

    [Fact]
    public async Task GetDocuments_DistinctSuggestions()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Invoice 2024.pdf");
        await UploadAndWaitAsync(client, "Invoice 2024.pdf");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=invioce", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResultWithSuggestions<DocumentListItem>>(CT);
        var matching = body!.Suggestions.Where(s => s == "Invoice 2024.pdf").ToList();
        Assert.Single(matching);
    }

    [Fact]
    public async Task GetDocuments_DeletedScope_SuggestsDeletedDocs()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Invoice 2024.pdf");

        var deleteResponse = await client.DeleteAsync($"/api/v1/documents/{docId}", CT);
        deleteResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Deleted=true&Search=invioce", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResultWithSuggestions<DocumentListItem>>(CT);
        Assert.Empty(body!.Items);
        Assert.Contains("Invoice 2024.pdf", body.Suggestions);
    }

    [Fact]
    public async Task GetDocuments_NoResults_SuggestsTagName()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Operating Agreement.pdf");
        await AssignTagAsync(client, docId, "Invoice");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=invioce", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResultWithSuggestions<DocumentListItem>>(CT);
        Assert.Empty(body!.Items);
        Assert.Contains("Invoice", body.Suggestions);
    }

    [Fact]
    public async Task GetDocuments_NoResults_SuggestsPartyName()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Operating Agreement.pdf");
        var party = await CreatePartyAsync(client, "Acme");
        await AssignPartyAsync(client, docId, party.Id);

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=acmee", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResultWithSuggestions<DocumentListItem>>(CT);
        Assert.Empty(body!.Items);
        Assert.Contains("Acme", body.Suggestions);
    }

    [Fact]
    public async Task GetDocuments_TagNotAttachedToScopedDoc_NotSuggested()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Operating Agreement.pdf");
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/documents/tags", new { path = "Invoice" }, CT);
        createResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=invioce", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResultWithSuggestions<DocumentListItem>>(CT);
        Assert.Empty(body!.Items);
        Assert.DoesNotContain("Invoice", body.Suggestions);
    }

    [Fact]
    public async Task GetDocuments_DuplicateNameAcrossSources_DedupedToOne()
    {
        var client = await CreateAdminClientAsync();
        var taggedDocId = await UploadAndWaitAsync(client, "Operating Agreement.pdf");
        await AssignTagAsync(client, taggedDocId, "Invoice");
        await UploadAndWaitAsync(client, "Invoice");

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=invioce", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResultWithSuggestions<DocumentListItem>>(CT);
        Assert.Empty(body!.Items);
        var matching = body.Suggestions.Where(s => s == "Invoice").ToList();
        Assert.Single(matching);
    }

    [Fact]
    public async Task GetDocuments_TagPartyDoc_RankedByScore()
    {
        // "mazdo" overlaps strongly with the "Mazda" window in the doc name and weakly with
        // "Amazon" in the tag/party names. The doc name should rank first regardless of the
        // source the suggestion came from.
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Mazda Receipt.pdf");
        var doc2Id = await UploadAndWaitAsync(client, "Operating Agreement.pdf");
        await AssignTagAsync(client, doc2Id, "Amazon");
        var party = await CreatePartyAsync(client, "Amazonian");
        await AssignPartyAsync(client, doc2Id, party.Id);

        var response = await client.GetAsync(
            "/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=mazdo", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResultWithSuggestions<DocumentListItem>>(CT);
        Assert.Empty(body!.Items);
        var ordered = body.Suggestions.ToList();
        Assert.Contains("Mazda Receipt.pdf", ordered);
        // "Amazon"/"Amazonian" overlap is borderline against the 0.3 threshold and may not
        // surface; if they do, they must rank below the doc-name match. The unconditional
        // tag/party surfacing guarantee is covered by SuggestsTagName / SuggestsPartyName.
        var mazdaIndex = ordered.IndexOf("Mazda Receipt.pdf");
        var amazonTagIndex = ordered.IndexOf("Amazon");
        var amazonianPartyIndex = ordered.IndexOf("Amazonian");
        if (amazonTagIndex >= 0)
        {
            Assert.True(mazdaIndex < amazonTagIndex,
                $"Expected 'Mazda Receipt.pdf' to outrank 'Amazon'. Got: [{string.Join(", ", ordered)}]");
        }
        if (amazonianPartyIndex >= 0)
        {
            Assert.True(mazdaIndex < amazonianPartyIndex,
                $"Expected 'Mazda Receipt.pdf' to outrank 'Amazonian'. Got: [{string.Join(", ", ordered)}]");
        }
    }

    private async Task AssignTagAsync(HttpClient client, Guid docId, string tagPath)
    {
        var getResponse = await client.GetAsync($"/api/v1/documents/{docId}", CT);
        getResponse.EnsureSuccessStatusCode();
        var doc = await getResponse.Content.ReadFromJsonAsync<DocumentDetailForAssign>(CT);
        var paths = doc!.Tags.Select(t => t.Path).Append(tagPath).Distinct().ToArray();

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/tagPaths", value = (object)paths }
        });
        patchResponse.EnsureSuccessStatusCode();
    }

    private async Task<PartyRef> CreatePartyAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/documents/parties", new { name }, CT);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PartyRef>(CT))!;
    }

    private async Task AssignPartyAsync(HttpClient client, Guid docId, Guid partyId)
    {
        var getResponse = await client.GetAsync($"/api/v1/documents/{docId}", CT);
        getResponse.EnsureSuccessStatusCode();
        var doc = await getResponse.Content.ReadFromJsonAsync<DocumentDetailForAssign>(CT);
        var ids = doc!.Parties.Select(p => p.Id).Append(partyId).Distinct().ToArray();

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/partyIds", value = (object)ids }
        });
        patchResponse.EnsureSuccessStatusCode();
    }

    private record PartyRef(Guid Id, string Name);
    private record TagRef(Guid Id, string Path);
    private record DocumentDetailForAssign(Guid Id, List<TagRef> Tags, List<PartyRef> Parties);
}
