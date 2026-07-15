using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class TextExtractionEngineTests : IntegrationTestBase
{
    public TextExtractionEngineTests(PostgresFixture fixture) : base(fixture) { }

    private record EngineItem(string Key, string Name, string Type, bool IsConfigured, string? ConfigurationError);

    // The factory configures no Ocr:Engines section at all, which is exactly the zero-config case: the
    // built-in tesseract instance is seeded and nothing else exists.
    [Fact]
    public async Task GetEngines_WithNoOcrConfig_ReturnsOnlyTheBuiltInTesseract()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/documents/text-extraction-engines", CT);
        response.EnsureSuccessStatusCode();

        var engines = await response.Content.ReadFromJsonAsync<List<EngineItem>>(CT);

        var engine = Assert.Single(engines!);
        Assert.Equal("tesseract", engine.Key);
        Assert.Equal("Tesseract", engine.Type);
        Assert.True(engine.IsConfigured);
        Assert.Null(engine.ConfigurationError);
    }

    // Mapped on the v1 group rather than under /settings (which is admin-only): the reprocess dialogs
    // need this list, and they are RequireContributor.
    [Fact]
    public async Task GetEngines_Contributor_IsAllowed()
    {
        var client = await CreateUserClientAsync(role: "Contributor");

        var response = await client.GetAsync("/api/v1/documents/text-extraction-engines", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // A Viewer cannot reprocess anything, so has nothing to pick an engine for.
    [Fact]
    public async Task GetEngines_Viewer_Returns403()
    {
        var client = await CreateViewerClientAsync();

        var response = await client.GetAsync("/api/v1/documents/text-extraction-engines", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetEngines_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/documents/text-extraction-engines", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Guid> GetLatestVersionIdAsync(HttpClient client, Guid docId)
    {
        var response = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        response.EnsureSuccessStatusCode();
        var versions = await response.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        return versions!.Items.First().Id;
    }

    // Rejected synchronously at enqueue, not by the job: no engine named "azure-layout" exists here.
    [Fact]
    public async Task ReprocessVersion_UnknownEngineKey_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);
        var versionId = await GetLatestVersionIdAsync(client, docId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/documents/{docId}/versions/{versionId}/reprocess",
            new { engineKey = "azure-layout" }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BulkReprocess_UnknownEngineKey_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/v1/documents/bulk-reprocess",
            new { ids = new[] { docId }, engineKey = "azure-layout" }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // An omitted body has always been legal and must keep meaning "use the household default".
    [Fact]
    public async Task ReprocessVersion_NoBody_StillAccepted()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);
        var versionId = await GetLatestVersionIdAsync(client, docId);

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/versions/{versionId}/reprocess", null, CT);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<JobResponse>(CT);
        await WaitForJobCompletionAsync(job!.JobId);
    }

    private record ProvenanceItem(
        string Engine, string EngineKey, DateTimeOffset ExtractedAt,
        string? RequestedEngineKey, string? FallbackReason,
        string? Language, int? PagesFromTextLayer, int? PagesOcred,
        string? ModelId, int? PageCount);

    private record DocumentWithProvenance(Guid Id, string? ExtractedText, ProvenanceItem? TextExtraction);

    private record VersionWithProvenance(Guid Id, string? ExtractedText, ProvenanceItem? TextExtraction);

    // Provenance is written on the upload path, through a real jsonb round-trip. That round-trip is the
    // point: Postgres reorders jsonb keys, so a payload that serializes correctly can still fail to read
    // back — which no in-memory test can catch.
    [Fact]
    public async Task Upload_RecordsExtractionProvenance_AndItSurvivesTheJsonbRoundTrip()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var response = await client.GetAsync($"/api/v1/documents/{docId}", CT);
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<DocumentWithProvenance>(CT);

        var provenance = document!.TextExtraction;
        Assert.NotNull(provenance);
        Assert.Equal("tesseract", provenance.Engine);
        Assert.Equal("tesseract", provenance.EngineKey);
        Assert.Equal("eng", provenance.Language);
        Assert.NotEqual(default, provenance.ExtractedAt);

        // Nothing was substituted: the household default resolved to a usable engine.
        Assert.Null(provenance.RequestedEngineKey);
        Assert.Null(provenance.FallbackReason);

        // Azure-only fields stay absent on the flattened DTO.
        Assert.Null(provenance.ModelId);
        Assert.Null(provenance.PageCount);
    }

    [Fact]
    public async Task Reprocess_RecordsProvenanceOnTheVersion()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);
        var versionId = await GetLatestVersionIdAsync(client, docId);

        var reprocess = await client.PostAsJsonAsync(
            $"/api/v1/documents/{docId}/versions/{versionId}/reprocess",
            new { engineKey = "tesseract" }, CT);
        reprocess.EnsureSuccessStatusCode();
        var job = await reprocess.Content.ReadFromJsonAsync<JobResponse>(CT);
        await WaitForJobCompletionAsync(job!.JobId);

        var response = await client.GetAsync($"/api/v1/documents/{docId}/versions/{versionId}", CT);
        response.EnsureSuccessStatusCode();
        var version = await response.Content.ReadFromJsonAsync<VersionWithProvenance>(CT);

        var provenance = version!.TextExtraction;
        Assert.NotNull(provenance);
        Assert.Equal("tesseract", provenance.EngineKey);
        Assert.Null(provenance.RequestedEngineKey);

        // The sample is a scanned PDF with no embedded text layer, so every page took the OCR branch.
        // This pair is what makes the hybrid path's per-page decision visible after the fact.
        Assert.NotNull(provenance.PagesOcred);
        Assert.NotNull(provenance.PagesFromTextLayer);
    }

    // Provenance describes the extraction that produced the text. Once a human edits that text, the
    // metadata would be describing text this row no longer holds — so it is cleared rather than left to
    // attribute a hand-written edit to Tesseract, page counts and all.
    [Fact]
    public async Task PatchVersion_EditingExtractedTextByHand_ClearsStaleProvenance()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);
        var versionId = await GetLatestVersionIdAsync(client, docId);

        var before = await client.GetFromJsonAsync<VersionWithProvenance>(
            $"/api/v1/documents/{docId}/versions/{versionId}", CT);
        Assert.NotNull(before!.TextExtraction);

        var patch = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}/versions/{versionId}", new[]
        {
            new { op = "replace", path = "/extractedText", value = "text a human typed" }
        });
        patch.EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<VersionWithProvenance>(
            $"/api/v1/documents/{docId}/versions/{versionId}", CT);

        Assert.Equal("text a human typed", after!.ExtractedText);
        Assert.Null(after.TextExtraction);
    }

    // Editing something else on the version must not throw the provenance away.
    [Fact]
    public async Task PatchVersion_EditingOnlyTheLabel_KeepsProvenance()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);
        var versionId = await GetLatestVersionIdAsync(client, docId);

        var patch = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}/versions/{versionId}", new[]
        {
            new { op = "replace", path = "/label", value = "Renamed" }
        });
        patch.EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<VersionWithProvenance>(
            $"/api/v1/documents/{docId}/versions/{versionId}", CT);

        Assert.NotNull(after!.TextExtraction);
        Assert.Equal("tesseract", after.TextExtraction.EngineKey);
    }

    [Fact]
    public async Task DocumentSettings_UnknownDefaultEngineKey_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await SendJsonPatchAsync(client, "/api/v1/documents/settings", new[]
        {
            new { op = "replace", path = "/defaultEngineKey", value = "azure-typo" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DocumentSettings_ConfiguredDefaultEngineKey_RoundTrips()
    {
        var client = await CreateAdminClientAsync();

        var response = await SendJsonPatchAsync(client, "/api/v1/documents/settings", new[]
        {
            new { op = "replace", path = "/defaultEngineKey", value = "tesseract" }
        });
        response.EnsureSuccessStatusCode();

        var patched = await response.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Equal("tesseract", patched!.DefaultEngineKey);

        var get = await client.GetAsync("/api/v1/documents/settings", CT);
        var fetched = await get.Content.ReadFromJsonAsync<DocumentSettingsItem>(CT);
        Assert.Equal("tesseract", fetched!.DefaultEngineKey);
    }
}
