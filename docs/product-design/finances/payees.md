# Finances — Payees

A **payee** is the counterparty on a flow — the merchant, biller, employer, or person the money went to or came from ("Blue Bottle Coffee", "City Hydro", "Acme Payroll"). It is the *who*, distinct from the *what* (the [category](overview.md#categorisation)). Payees are a **global**, household-wide lookup (no per-household column, mirroring how categories are shared), managed at `/finances/payees` and picked inline on any flow.

This doc covers the payee entity and picker, and the **statement-import learning subsystem** that makes the payee the primary attribution on import and remembers each confirmed mapping so future imports pre-fill it.

## The entity

`Payee` (`finances.payees`) is a plain entity:

- `Name` — unique case-insensitively (a `lower("Name")` functional unique index; a friendly 409 precedes the DB backstop).
- `DefaultCategoryId` → an Income/Expense category `Account` (optional). Drives **default-category autofill**: picking a payee on a flow can pre-select its usual category, and statement import auto-categorises from it (see below).
- `Notes`, `IsArchived` — archiving hides a payee from pickers/lists but keeps it on historical transactions (the retire path for an in-use payee, which cannot be deleted).
- Provenance (`CreatedBy`/`UpdatedBy`), timestamps.

`NormalTransaction.PayeeId` is the (nullable, `Restrict`-on-delete) attribution. A payee referenced by any transaction cannot be deleted — **archive** or **merge** instead.

### The picker

`app-payee-picker` is the shared control: server-searched, debounced, with inline **create** ("Create 'X'") when the typed name is new. A **dedupe nudge** ("Did you mean **{existing}**?") appears when the typed name is a case-insensitive *substring* of (or superstring of) an existing payee — catching near-duplicates ("Blue Bottle" vs "Blue Bottle Coffee") before minting a new one. It emits the full payee so callers can read the default category for autofill.

## Statement-import learning

On statement import the **payee is the primary attribution**, and each confirmed `raw-description → payee` mapping is **learned** so future imports pre-fill suggestions — improving with every statement. The raw statement line (an ugly, per-swipe-unique bank string like `SQ *BLUE BOTTLE 8005551234 SF`) is the payee *candidate*, never an authored field; the editable **memo** is pre-filled with the raw text, but learning always keys on the **server-side staged raw text**, so editing the memo never corrupts what's learned.

### Suggestion tiers (precedence order)

1. **Learned** — a deterministic, high-trust hit from the learning table (`finances.payee_mappings`). Pre-applied client-side, marked "learned".
2. **AI → existing payee** — the AI second-pass resolved the line to a payee already in the catalog. A **hint-only ghost** (sparkle + *Accept*), not applied until accepted.
3. **AI → new payee** — the AI proposed a *new* payee name. A ghost with **Create & use** (one click mints it), plus the picker's dedupe nudge.
4. **Manual** — the reviewer picks (or clears) a payee themselves.

A payee's default category rides along to also **auto-categorise** the line (below).

### Hint-only, and the Import gate

An un-accepted AI suggestion (tiers 2/3) is a **ghost preview** — never auto-applied, never learned. Accepting is a cheap explicit gesture (per-row *Accept* / *Create & use*, or **"Accept all suggestions (N)"**). On **Import**, if any un-reviewed AI ghost remains on a line being committed, a **warning dialog** gates:

- **Review them** — filters the table to those rows.
- **Apply all & import** — applies those payees but with **`Learn = false`** (apply-but-don't-learn).

Tier-1 learned pre-fills and manual picks never trigger the gate.

### Learn only on an explicit signal

This is the anti-entrenchment guardrail. A mapping is written (upserted) **only** when the reviewer explicitly set the payee this session — a **pick, create, accept, or override** (the per-line `Learn` flag on commit). A rubber-stamped tier-1 pre-fill, a bulk-assigned gap-fill that matched an existing pick, or a dialog-forced "Apply all & import" is **applied but not taught**. So a wrong guess that was silently carried through never entrenches itself.

### The normalizer (the learning key)

`PayeeDescriptionNormalizer` is a pure, deterministic, **versioned** function turning a raw description into a fingerprint key:

- InvariantCulture uppercase → Unicode **NFKC** fold → strip a leading asterisk-delimited processor token (`SQ *`, `TST*`, `PP *`) → strip a trailing store/ref number (`#1234`, ` 004512`) → collapse punctuation/whitespace → trim.
- **Embedded digits survive** (`7-ELEVEN`, `84 LUMBER`).
- A degenerate result — empty, below a small min-letter floor, or matching a tiny boilerplate stoplist (`POS PURCHASE`, `INTERAC`, `E-TRANSFER`, `BILL PAYMENT`, `ATM WITHDRAWAL`, `INTEREST`, …) — normalizes to **null**, which is **never learned and never matched**.
- `NormalizationVersion` is stored on every mapping row so a future rule change is a planned re-key (the stored `SampleRawDescription` powers the re-normalization), not silent orphaning.

The mapping is an `INSERT … ON CONFLICT ("NormalizedKey") DO UPDATE` upsert (globally unique key): a new key inserts with `TimesConfirmed = 1`; a repeat re-points to the freshly-confirmed payee and bumps the count. Keys are de-duped within one commit (last-wins), so a repeated line bumps once.

### Auto-categorise from payee

When a line carries a payee, the payee's **default category** is applied to also categorise the flow — best-effort and **pre-filtered**: only when the category exists, is a live (non-archived) category account, and its type matches the line's direction; otherwise the line falls back to Uncategorised. This never fails the atomic commit (an archived/mismatched category degrades silently rather than throwing). Shown as a **read-only category chip** under the payee. Gated by a global setting **`AutoCategoriseFromPayee`** (default **on**), on the Finances settings.

### Rollback retracts what it taught

Rollback of an import (`DELETE /finances/imports/{documentId}`) also **retracts the mappings that import taught**, matched precisely on `LastWriterDocumentId`. A key later overwritten by a *different* import carries a different id and is left intact — so rollback never un-learns another statement's confirmation.

### AI as a separate second pass

The AI resolution (tiers 2/3) is a **separate, small, non-streaming** call (`IPayeeResolutionService`), distinct from the streaming statement extraction — isolating the risk of overflowing the already-long extraction call. It runs only when AI is configured; with **AI off**, tiers 2/3 simply vanish and the feature still works: manual picks (tier 4) teach the table, and tier-1 suggestions fire on the next import. The AI pass is best-effort — a failure leaves tier-1 intact and never fails the import.

## Merge

Payee **merge** (Brand-style) is the cleanup valve for near-duplicates (especially AI-created ones). `POST /finances/payees/{sourceId}/merge` with a target: it re-points every transaction **and** every learned mapping from source to target, then deletes the source (audited). Entry points:

- The payee list's row **Merge into…** action → a target-picker dialog.
- The edit dialog's **rename → 409 → merge** handoff: renaming a payee to a name that already exists offers "Merge this payee into it".

## See / delete learned mappings

The payee **edit dialog** surfaces a "**Learned from these descriptions**" section (`GET /finances/payees/{id}/mappings`) listing each learned sample with its confirmation count, and a ✕ to prune a wrong or stale fingerprint (`DELETE /finances/payee-mappings/{id}`, audited). Machine-written learning rows are not audited; explicit human deletes and merges are.

## Deliberately out of scope (v1)

Fuzzy/similarity matching beyond exact normalized-key equality; a standalone mappings-management page (the payee edit dialog is the v1 surface); per-household payee scoping; transfer/counterparty inference from the payee.
