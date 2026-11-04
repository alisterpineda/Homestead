using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Modules.Documents.Features.Versions;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

// The editor's name is the one thing a manual record cannot carry (the version row has no user FK and a
// persisted name would rot on rename), so the reader resolves it per read. A hand edit by a user who has
// since been hard-deleted leaves a dangling id in jsonb — that must read as "Someone", never throw.
public class TextExtractionProvenanceReaderTests : IDisposable
{
    private static readonly DateTimeOffset EditedAt = new(2026, 11, 4, 9, 15, 0, TimeSpan.Zero);

    private readonly TestAppDbContext _db = TestAppDbContext.Create();

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> SeedUserAsync(string displayName, string email)
    {
        var user = new AppUser
        {
            Email = email,
            DisplayName = displayName,
            Role = AppRole.Contributor,
            IsActive = true,
            CreatedAt = EditedAt
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user;
    }

    private static string? Manual(Guid? editor) => TextExtractionMetadata.Serialize(
        new ManualTextExtractionMetadata { ExtractedAt = EditedAt, EditedByUserId = editor });

    private Task<TextExtractionProvenanceResponse?> ReadAsync(string? json) =>
        TextExtractionProvenanceReader.ReadAsync(_db, json, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Manual_NamesTheEditor()
    {
        var user = await SeedUserAsync("Pat Proofreader", "pat@example.com");

        var response = await ReadAsync(Manual(user.Id));

        Assert.Equal(("manual", "Pat Proofreader"), (response!.Engine, response.EditedByDisplayName));
    }

    // AppUser.DisplayName defaults to "" rather than null, so a `??` chain would never reach the email.
    [Fact]
    public async Task Manual_BlankDisplayName_FallsBackToEmail()
    {
        var user = await SeedUserAsync("  ", "pat@example.com");

        var response = await ReadAsync(Manual(user.Id));

        Assert.Equal("pat@example.com", response!.EditedByDisplayName);
    }

    [Fact]
    public async Task Manual_DeletedEditor_ReadsAsSomeone()
    {
        var response = await ReadAsync(Manual(Guid.NewGuid()));

        Assert.Equal(TextExtractionProvenanceReader.UnknownEditor, response!.EditedByDisplayName);
    }

    [Fact]
    public async Task Manual_NoEditorRecorded_LeavesTheNameNull()
    {
        var response = await ReadAsync(Manual(editor: null));

        Assert.Equal("manual", response!.Engine);
        Assert.Null(response.EditedByDisplayName);
    }

    [Fact]
    public async Task EngineRun_DoesNotLookAnyoneUp()
    {
        var json = TextExtractionMetadata.Serialize(new TesseractTextExtractionMetadata
        {
            EngineKey = "tesseract", ExtractedAt = EditedAt, Language = "eng"
        });

        var response = await ReadAsync(json);

        Assert.Equal(("tesseract", (string?)null), (response!.Engine, response.EditedByDisplayName));
    }

    [Fact]
    public async Task MissingOrUnreadable_ReturnsNull()
    {
        Assert.Null(await ReadAsync(null));
        Assert.Null(await ReadAsync("""{"engine":"some-engine-we-removed"}"""));
    }
}
