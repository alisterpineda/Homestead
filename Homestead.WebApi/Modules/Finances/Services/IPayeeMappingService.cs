namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// The learning table's write/read seam: upsert a confirmed <c>raw-description → payee</c> mapping, look up
/// learned payees at parse time (tier-①), and retract an import's mappings on rollback. Machine-written rows
/// are deliberately not audited. All writes auto-enroll into the ambient command transaction via the
/// <c>CommandTransactionInterceptor</c> — no manual transaction ceremony.
/// </summary>
public interface IPayeeMappingService
{
    /// <summary>
    /// Learn (or re-confirm) that <paramref name="normalizedKey"/> maps to <paramref name="payeeId"/>. A raw
    /// <c>INSERT … ON CONFLICT ("NormalizedKey") DO UPDATE</c> upsert: a new key inserts with
    /// <c>TimesConfirmed = 1</c>; an existing key re-points to the new payee and bumps <c>TimesConfirmed</c>.
    /// <paramref name="documentId"/> is stamped as <c>LastWriterDocumentId</c> so rollback can retract
    /// precisely. Only ever called with a non-null key (the caller drops null-normalizing lines).
    /// </summary>
    Task UpsertAsync(string normalizedKey, Guid payeeId, string sampleRawDescription, Guid documentId, CancellationToken ct);

    /// <summary>
    /// Delete every mapping this document's import last wrote (matched on <c>LastWriterDocumentId</c>).
    /// Precise by construction: a key later overwritten by a different import carries a different id and is
    /// left untouched. Returns the number of retracted rows.
    /// </summary>
    Task<int> RetractByDocumentAsync(Guid documentId, CancellationToken ct);
}
