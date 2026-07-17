using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Homestead.WebApi.Domain.Auditing;

namespace Homestead.WebApi.Domain.Finances;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TransactionDocumentLinkOrigin
{
    Manual,
    StatementImport,
}

/// <summary>
/// Links a transaction to a document (receipt, invoice, source statement). One mechanism, two
/// origins: statement-import commit writes <see cref="TransactionDocumentLinkOrigin.StatementImport"/>
/// links (which double as the re-import guard and rollback scope), users write Manual ones.
/// </summary>
public class TransactionDocumentLink
{
    public Guid AccountTransactionId { get; set; }
    public AccountTransaction AccountTransaction { get; set; } = null!;

    // Real FK to documents.documents (ON DELETE CASCADE — link rows die with the document), but
    // deliberately NO navigation property: Document carries a global DeletedAt query filter, and a
    // required nav to a filtered principal silently drops trashed documents from joins. Reads must
    // resolve documents via IDocumentQueryService.AccessibleDocuments(TrashFilter.IncludeTrashed).
    public Guid DocumentId { get; set; }

    public TransactionDocumentLinkOrigin Origin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    // Composite-key entity → deterministic synthetic id for audit rows, which need a single Guid
    // EntityId (precedent: AppSettingSnapshot.KeyToGuid).
    public static Guid SyntheticId(Guid transactionId, Guid documentId) =>
        DeterministicGuid.FromKey($"{transactionId:N}:{documentId:N}");

    /// <summary>
    /// The single owner of "which links constitute this document's active import". The re-import
    /// guards (initiate pre-check + authoritative commit check) and the rollback scope all filter
    /// through this — they must never drift apart, or a document could report "already imported"
    /// while rollback finds nothing to undo. Origin-scoped so manual links never count.
    /// </summary>
    public static Expression<Func<TransactionDocumentLink, bool>> IsImportOriginFor(Guid documentId) =>
        l => l.DocumentId == documentId && l.Origin == TransactionDocumentLinkOrigin.StatementImport;
}
