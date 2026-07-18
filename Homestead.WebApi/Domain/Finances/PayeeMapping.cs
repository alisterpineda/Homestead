using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Finances;

/// <summary>
/// A learned <c>raw-statement-description → payee</c> mapping. Global scope (matching <see cref="Payee"/> —
/// no household column). The key is a deterministic fingerprint of a statement line's raw description
/// (see <c>PayeeDescriptionNormalizer</c>), so a future import of the same counterparty pre-fills the payee.
/// Written only on an explicit reviewer signal (pick / create / accept / override) — a rubber-stamped or
/// dialog-forced payee is applied but never taught, the anti-entrenchment guardrail. Machine-written rows are
/// deliberately <b>not</b> audited (manual delete / merge of a payee are, via their own handlers).
/// </summary>
public class PayeeMapping
{
    public Guid Id { get; set; }

    /// <summary>The deterministic fingerprint the mapping keys on — unique. Never null (a null normalization
    /// result is neither learned nor matched).</summary>
    public required string NormalizedKey { get; set; }

    /// <summary>The normalizer version that produced <see cref="NormalizedKey"/>, so a future normalizer
    /// change is a planned re-key rather than silent orphaning.</summary>
    public short NormalizationVersion { get; set; }

    public Guid PayeeId { get; set; }
    public Payee? Payee { get; set; }

    /// <summary>One raw example of a description that reduced to <see cref="NormalizedKey"/> — powers the
    /// management UI and a future re-normalization migration.</summary>
    public required string SampleRawDescription { get; set; }

    /// <summary>Bumped only on an explicit confirmation signal; not load-bearing for v1 exact-match.</summary>
    public int TimesConfirmed { get; set; }

    /// <summary>The document whose import last wrote this key (no FK) — enables precise rollback-retract:
    /// a key overwritten by a later import carries a different id and is left untouched.</summary>
    public Guid? LastWriterDocumentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
