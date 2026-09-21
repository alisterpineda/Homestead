using Homestead.WebApi.Authentication;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public class PayeeMappingService(AppDbContext db, ICurrentUserAccessor currentUser)
    : IPayeeMappingService
{
    // Column length ceiling (matches PayeeMappingConfiguration). Raw descriptions are already short, but the
    // upsert is raw SQL with no EF length validation, so guard against a stray oversized value.
    private const int MaxKeyLength = 512;

    public async Task UpsertAsync(
        string normalizedKey, Guid payeeId, string sampleRawDescription, Guid documentId, CancellationToken ct)
    {
        var key = Truncate(normalizedKey);
        var sample = Truncate(sampleRawDescription);
        var userId = currentUser.UserId;
        // Raw SQL never goes through the change tracker, so UuidV7ValueGenerator can't run here.
        var id = Guid.CreateVersion7();
        short version = PayeeDescriptionNormalizer.Version;

        // First ON CONFLICT upsert in the app. The CommandWriteCommandInterceptor auto-enrolls this raw
        // non-query into the command transaction (re-binding it onto the owned tx), so it's atomic with the
        // flows written in the same handler — no manual BeginTransaction. On conflict the existing row
        // re-points to the freshly-confirmed payee and bumps TimesConfirmed off the *stored* value.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO finances.payee_mappings
                ("Id", "NormalizedKey", "NormalizationVersion", "PayeeId", "SampleRawDescription",
                 "TimesConfirmed", "LastWriterDocumentId", "CreatedAt", "UpdatedAt",
                 "CreatedByUserId", "UpdatedByUserId")
            VALUES
                ({id}, {key}, {version}, {payeeId}, {sample},
                 1, {documentId}, now(), now(),
                 {userId}, {userId})
            ON CONFLICT ("NormalizedKey") DO UPDATE SET
                "PayeeId" = EXCLUDED."PayeeId",
                "SampleRawDescription" = EXCLUDED."SampleRawDescription",
                "NormalizationVersion" = EXCLUDED."NormalizationVersion",
                "TimesConfirmed" = finances.payee_mappings."TimesConfirmed" + 1,
                "LastWriterDocumentId" = EXCLUDED."LastWriterDocumentId",
                "UpdatedByUserId" = EXCLUDED."UpdatedByUserId",
                "UpdatedAt" = now();
            """, ct);
    }

    public Task<int> RetractByDocumentAsync(Guid documentId, CancellationToken ct) =>
        db.PayeeMappings
            .Where(m => m.LastWriterDocumentId == documentId)
            .ExecuteDeleteAsync(ct);

    private static string Truncate(string value) =>
        value.Length <= MaxKeyLength ? value : value[..MaxKeyLength];
}
