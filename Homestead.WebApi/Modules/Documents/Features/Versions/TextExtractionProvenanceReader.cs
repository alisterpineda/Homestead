using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Versions;

/// <summary>
/// Reads a version's provenance for an API response. The pure mapping is
/// <see cref="TextExtractionProvenanceResponse.From(string?)"/>; this adds the one thing the blob cannot
/// carry, the editor's current display name, which a manual record stores only as a user id (the
/// version row has no user foreign key, and a persisted name would rot on rename).
/// </summary>
public static class TextExtractionProvenanceReader
{
    public const string UnknownEditor = "Someone";

    public static async Task<TextExtractionProvenanceResponse?> ReadAsync(
        AppDbContext db, string? json, CancellationToken ct)
    {
        var metadata = TextExtractionMetadata.Deserialize(json);
        var response = TextExtractionProvenanceResponse.From(metadata);

        if (response is null || metadata is not ManualTextExtractionMetadata { EditedByUserId: { } editorId })
            return response;

        // Same fallback order as the activity feed (display name, then email, then a placeholder): a
        // deleted user still reads as a person, not a blank.
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == editorId)
            .Select(u => new { u.DisplayName, u.Email })
            .FirstOrDefaultAsync(ct);

        var name = user is null ? null
            : !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName
            : !string.IsNullOrWhiteSpace(user.Email) ? user.Email
            : null;

        return response with { EditedByDisplayName = name ?? UnknownEditor };
    }
}
