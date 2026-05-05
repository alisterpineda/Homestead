using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

/// <summary>
/// Covers the fuzzy "Did you mean…?" suggestions surfaced when document search returns no results.
/// Access scoping is reused unchanged from the main query, so it is not re-verified here —
/// see <c>PartyVisibilityTests</c> / <c>TagVisibilityTests</c> for that coverage.
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
}
