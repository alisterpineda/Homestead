# Documents — Requirements

This document describes the Documents module within Homestead: what it is, what it does, what it does not do, and the design decisions that shape it. It is product-level, not implementation-level — see the repo's CLAUDE.md files for code structure and framework choices.

Documents sits alongside Finances and the cross-cutting infrastructure modules listed in the top-level [SKILL.md](../../SKILL.md). This file governs *what* the module should do; defer to existing module patterns in the codebase for *how* it is wired up.

## Purpose

The Documents module is the household's durable, searchable archive of files — receipts, statements, contracts, IDs, warranty paperwork, anything worth keeping. It models each file as a versioned **Document** with metadata (name, document date, expiry date, notes), tagged into a hierarchy, and linked to the people or organisations the document concerns. The primary outcome is a household archive where any document can be retrieved months or years later by name, by content, or by who/what it relates to, without depending on the user remembering where they put it.

The module is not a collaborative editor, a signing platform, a workflow tool, or a full DMS. It captures and organises files; it does not transform their contents.

## In scope for v1

- Upload of arbitrary file types, with thumbnail generation and best-effort text extraction.
- Document versioning: each upload supersedes the prior version while preserving full history.
- Hierarchical, path-delimited tags with user-controllable colours and inheritance.
- Linked **parties** — people, organisations, or other entities a document concerns.
- A user-visible activity stream that interleaves field-level audit log entries and user comments.
- Trash with restore and permanent-delete (soft-delete only for documents; everywhere else delete is destructive).
- Bulk operations: bulk soft-delete, bulk metadata edit (tags and parties only), bulk reprocess, bulk classify, bulk download.
- PostgreSQL full-text search across document name (weighted A), the latest version's extracted text (weighted B), and document summary (weighted C), with ILIKE fallbacks on name and extracted text, and prefix-expansion on user input. Zero-result searches surface fuzzy "Did you mean…?" suggestions over document names via `pg_trgm`.
- ZIP download organised by tag path, with a dedicated `_Untagged/` folder and party names embedded in filenames.
- Household-wide choice of text-extraction engine, with a built-in fallback.
- AI-powered document classification: user-configured LLM providers classify documents by selecting matching tags, generating a summary, and suggesting a title. Tags and name are auto-applied with per-field approve/decline controls via a generic suggestion system; summaries are applied directly without suggestion records (no approve/decline — the summary is purely AI-managed).

## Out of scope for v1

- Configurable retention policies and auto-purge — both trash and version history accumulate indefinitely. (Planned, see *Future-compatibility*.)
- Editing or deleting comments — comments are immutable in v1. (Planned, see *Future-compatibility*.)
- Bulk-editing fields beyond tags and parties (no bulk rename, bulk-set date, bulk-set notes). (Planned, see *Future-compatibility*.)
- Document grouping / scoping beyond a single household — the data model carries a `GroupId` field, plumbed through endpoints and filters, but it is not user-facing in v1. Reserved for future use, not necessarily multi-household.
- In-document collaborative editing, signing flows, approval workflows.
- Inline preview / annotation beyond a thumbnail.
- Version diffs and version revert. (Version *delete* is supported — see [versions.md](versions.md) → *Version delete*.)
- Document-to-document relationships (supersedes / derived-from / etc.) beyond the party link.
- Automated party detection.
- A user-facing field-level change-history view (the audit log already exists and powers the activity stream, but per-field diffs are not surfaced as a separate history surface).
- Multi-user permissions / per-document ACLs.

These are excluded from v1 but the model must not block adding them later.

## User-facing concepts

### Document

A document is the unit the user thinks about — "my 2024 lease," "passport," "Q3 brokerage statement." It carries identity and metadata at the document level (Name, Notes, optional DocumentDate, optional ExpiryDate, optional Summary), a set of Tags, a set of linked Parties, and a chain of Versions. Document-level fields are independent of which file is currently the latest version: re-uploading does not re-tag; renaming does not re-upload.

Documents soft-delete to Trash rather than disappearing. Audit fields (`CreatedAt`/`CreatedByUserId`, `UpdatedAt`/`UpdatedByUserId`, `OwnerUserId`, and on soft-delete `DeletedAt`/`DeletedByUserId`) are recorded but not editable.

See [versions.md](versions.md) for the document-scoped vs version-scoped split.

### Document version

Every uploaded file lands as a numbered version. The newest version is the document's current content; older versions are read-only references kept for history. File-level attributes (the blob itself, MIME type, size, extension, content hash, and extracted text) are version-scoped — replacing the file produces a new row, not a mutation of the existing one. A user-set `Label` on the version is mutable; `ExtractedText` is rewritten by reprocess.

Text extraction runs asynchronously after upload and is best-effort.

See [versions.md](versions.md) for the upload pipeline, the version-scoped/document-scoped boundary, reprocess semantics, and the text-extraction engine selection.

### Tag

Tags are organisational labels with a slash-delimited hierarchical path: a document tagged `Finances/Investments/2024` is automatically associated with all three levels of the hierarchy. Tags have a user-set colour that cascades to descendants when the descendant has no colour of its own. Documents and tags are many-to-many — one document can carry many tags; one tag labels many documents.

Tags are organisational, not authoritative. Whether a document "belongs to" a particular agency is captured by linking that agency as a Party; tags say where in the household's filing system the document lives.

See [tags.md](tags.md) for the path model, colour inheritance, descendant cascade on edit/delete, search expansion through child tags, and the role tags play in ZIP downloads.

### Party

A party is a person, organisation, or other entity that a document is *about* or *concerns* — "Our landlord," "Dr. Smith," "ACME Insurance," "the IRS." Parties are flat (no hierarchy) and uniformly typed (no person-vs-organisation distinction at the data layer). A document can link many parties; a party can be linked from many documents. Deleting a party unlinks it from any documents that referenced it; the documents themselves are unaffected.

Parties live inside the Documents module and have no meaning outside it in v1.

### Comment / Activity

A document has a single combined **activity stream** that interleaves two sources in chronological order:

- **Audit log entries** — system-generated, one per field-level change to the document or its versions (Name, DocumentDate, ExpiryDate, ExtractedText, TagPaths, PartyIds, Label). Recorded automatically on every mutation via the shared `auditing.audit_logs` table; field display names and human summaries are formatted at read time.
- **Comments** — user-authored free text. In v1 comments are immutable: they cannot be edited or deleted from the UI.

The activity stream is the user-facing projection of the audit log with comments interleaved. There is no separate field-level diff surface in v1.

### AI classification and suggestions

Documents can be classified by an AI provider — a user-configured LLM endpoint (Azure AI Foundry for v1). Classification takes the document's name and latest version's extracted text, along with all available tags, and returns three outputs: matching tag IDs, a summary, and a suggested title. Tags and name are **auto-applied** to the document immediately so the values are useful right away, and each is tracked by a **suggestion** record with approve/decline controls. Summaries are also auto-applied but are **not** tracked as suggestions — they are purely AI-managed and displayed as read-only text with no approve/decline UI.

The suggestion system is generic — `DocumentSuggestion` tracks AI-modified fields (Tags, Name) with `Pending → Approved | Declined` status. Approving a suggestion confirms the AI value (it stays applied; the AI indicator disappears). Declining reverts the change: for tags, the AI-applied tag is removed; for name, the document reverts to its original name. Approve-all and decline-all bulk actions are available when multiple suggestions are pending.

Re-classifying a document clears any prior pending suggestions (reverting their changes) before applying new ones. Manual edits to a field auto-decline its pending suggestion — the user has taken explicit ownership.

Classification runs as a background job and can be triggered three ways: manually from the document detail menu, as a bulk action from the list page, or automatically on upload when the household-wide `AutoClassifyOnUpload` setting is enabled and a default AI provider is configured. Auto-classify on upload is fire-and-forget — the upload completes immediately and classification runs as a separate job.

AI providers are managed in Admin (`/admin/ai-providers`) and follow the same CRUD pattern as text-extraction engines. Each provider has a type (Azure AI Foundry for v1), a JSON configuration block (endpoint, API key, deployment name), and an enabled/disabled toggle. The Documents settings page (`/documents/settings`) has a default AI provider picker and an auto-classify toggle. The suggestion system is designed to be extensible: adding new AI-suggested fields (document date, parties, expiry date) requires only a new enum value, a prompt update, and UI — no schema changes.

### Trash

Deleting a document soft-deletes it: the document gains a `DeletedAt` timestamp and disappears from the default list. Trash has a dedicated route (`/documents/trash`) that the user must navigate to explicitly. From Trash, a document can be **restored** (clears `DeletedAt`) or **permanently deleted** (removes the row and every version's file from disk). An "Empty Trash" affordance permanently deletes everything in Trash.

Trash retention is unbounded in v1 — nothing auto-purges. Configurable retention is planned. Soft-delete via Trash applies to documents only; tags, parties, comments, and individual versions delete destructively.

### Search

The list page is search-first: a single text box at the top drives both content search and result ranking. Behind the scenes the query runs in five OR'd modes — case-insensitive ILIKE on document name, case-insensitive ILIKE on any version's extracted text, case-insensitive ILIKE on attached tag names, case-insensitive ILIKE on linked party names, and PostgreSQL full-text search via `ts_rank_cd` against a weighted `tsvector` (Name = A, latest version's ExtractedText = B), with prefix-expansion on user input. The tag and party branches federate the search bar with the filter popover so typing `invoices` finds documents tagged **Invoice** without opening the filter. When a search term is present, results are ordered by FTS rank; otherwise by the user's chosen sort field. Filters layer on top of search. When the primary search returns no results, the list surfaces "Did you mean…?" chips drawn from a `pg_trgm` fuzzy match against document names — opt-in, never silent.

See [search.md](search.md) for the query mechanics, weighting, prefix expansion, sort interaction, and the filter taxonomy.

### Bulk operations

The list page has a selection mode toggled inline (not a separate page). With one or more documents selected the user can:

- **Bulk soft-delete** to Trash.
- **Bulk metadata edit**: add or remove tags; add or remove parties.
- **Bulk reprocess** the latest version of each document, optionally pinning a specific extraction engine for the run.
- **Bulk classify** via an AI provider, optionally pinning a specific provider for the run.
- **Bulk download** as a ZIP.

Bulk metadata edit uses tri-state controls — for each tag/party, the value can be "add to all," "remove from all," or "leave alone" — so existing per-document state is not overwritten unintentionally. Bulk metadata edit is intentionally narrow to taxonomy fields (tags, parties); document-level scalars (Name, Notes, DocumentDate, ExpiryDate) are not bulk-editable in v1.

### ZIP download

A bulk download enqueues a job that packages the selected documents into a single archive organised by tag path:

- A document tagged `Finances/Investments` is placed at `Finances/Investments/<filename>`.
- A document with multiple tags appears in **each** tag-path folder, with duplicated content. The duplication is intentional — each tag is an independent organisational view of the same document.
- A document with no tags is placed in a top-level `_Untagged/` folder.
- Linked party names are appended to the filename (`<docname> - <party1>, <party2><ext>`), truncated past 200 characters.
- Within any folder, filename collisions are resolved by appending a counter.

The job writes the resulting ZIP to a downloads area with a 7-day expiration; the client polls for completion before downloading. Only one active download per user is permitted at a time.

## Design principles

### File preservation is non-negotiable; extraction is best-effort

Every accepted upload produces a Document and a Version regardless of whether text extraction succeeds. If OCR fails, the version lands with `ExtractedText = null` and a warning is logged server-side; the user sees a normal successful upload. The document remains findable by name, tags, parties, and date — it just isn't full-text searchable until reprocessed. Explicit reprocess actions, in contrast, surface failures inline so the user knows their request didn't take effect.

The asymmetry is deliberate: uploads prioritise capturing the file safely; reprocess prioritises feedback because the user asked for it.

### Document scope vs version scope is load-bearing

What belongs to the document and what belongs to the version is a contract, not an implementation detail:

- **Document-scoped:** Name, Notes, DocumentDate, ExpiryDate, Summary, Tags, Parties, Suggestions, GroupId (deferred), trash state.
- **Version-scoped:** the file blob, ContentType, FileSize, FileExtension, FileHash, ExtractedText, Label.

This split is what makes "uploading a corrected version of the same document" coherent: metadata you have already curated stays attached to the document, while file content rolls forward. Code paths and UI must respect the split — moving a field across the boundary is a deliberate decision, not an offhand patch.

### Tags are organisational, parties are semantic

Tags say where in the filing system a document lives. Parties say what entities the document concerns. Both are many-to-many with documents, but they answer different questions and the UI keeps them visually distinct. Search expands tags through their hierarchy (a query for `Finances` finds anything tagged `Finances/Investments/2024` when descendant expansion is on); parties match exactly.

### Versioning is monotonic

A new upload always produces a new version with the next version number. Older versions are not replaced or rewritten by later uploads — the earlier rows persist with their file blobs and hashes immutable. The user can download an older version and edit its `Label`, but cannot replace its content. There is no version revert and no version diff in v1.

Versions can be explicitly deleted by the user except for the only remaining version of a document (see [versions.md](versions.md) → *Version delete*). Delete is the recovery path for "I uploaded the wrong intermediate state."

### Activity is the audit log made visible

The activity stream is not a separate "what happened" log; it is a curated read of the existing field-level audit log, interleaved with user comments. There is exactly one source of truth for "what changed and when" — the audit log — and the activity feed is its user-facing projection. New auditable events show up automatically in the feed once their field has a display name registered; nothing has to be double-written.

The audit log itself is written for every mutation across every module via the shared `IAuditLogger`. Documents is the only v1 module that surfaces it to users.

### Bulk operations are for taxonomy, not content

Bulk edit deliberately covers only tags and parties — adding and removing labels at scale, never rewriting per-document content. Renaming a document, editing its notes, or changing its document date should be a single-document action because the right value is per-document. The narrowness is the feature, not a gap; widening the bulk surface later is a deliberate expansion.

### No retention bounds on user-visible data

Trash never auto-purges. Old versions are never pruned. The audit log is retained indefinitely. Only the asynchronous download ZIPs have a retention rule (7 days). For a single household the storage cost of "keep everything forever" is small compared to the cost of an accidental purge; configurable retention is a v2 concern.

## Distinctive UX decisions worth preserving

1. **Search is the primary entry point on the list page**, not a sidebar filter. A single text box at the top of the list is the first thing the user reaches for; everything else (tag/party filters, sort) is secondary.
2. **Bulk operations are an inline mode toggle**, not a separate page. Selecting documents activates the bulk action bar in place; deselecting returns the list to its normal state. Bulk feels lightweight, not like a separate workflow.
3. **Tag colours render everywhere a tag appears** — list rows, detail page, filter popover, edit dialogs. Effective colour inherits from ancestors when the tag has no colour of its own, so colour-coding a folder hierarchy requires minimal upkeep.
4. **Trash is opt-in**: the default list shows only live documents; Trash lives on its own route. Users have to navigate to Trash to see deleted items. The default surface stays uncluttered.
5. **The activity stream interleaves comments and audit entries** rather than separating them. Users read "what happened to this document" as one chronological feed.
6. **Bulk metadata edits use tri-state controls** so leaving a tag/party alone is a first-class option distinct from adding or removing it. Users do not get surprised by accidentally clearing tags they never touched.
7. **OCR failure on upload is silent.** The document saves, the extracted-text section is empty, and the user notices when content search misses. This is a deliberate "don't block on extraction" stance; reprocess is the recovery path.
8. **AI suggestions integrate inline**, not as a separate panel or modal. The name input gains an AI badge with approve/decline buttons; AI-suggested tags render with a dashed border and sparkle icon with per-tag approve/decline; the summary appears as plain read-only text (no approve/decline — it is purely AI-managed). There is no "review suggestions" step — the values are already applied and the approve/decline controls are right where the values are displayed.
9. **AI-suggested tags are visually distinct** — dashed border, sparkle icon, and per-tag check/X buttons — so the user can tell at a glance which tags came from AI vs manual assignment.
10. **Approve-all / decline-all** appears as a compact inline action row when two or more suggestions are pending, providing a fast path for users who trust or distrust the classification wholesale.

## Future-compatibility

The following must be addable later without re-migrating documents, versions, tags, or audit history:

- **Configurable retention** for trash, old versions, and the audit log. The current "indefinite" stance lets retention be added as a policy applied at read/cleanup time without changing the data shape.
- **Editable / deletable comments.** Comments are persisted with author and timestamps; adding edit/delete is a UI and endpoint concern, not a data migration.
- **Wider bulk-edit surface** — bulk rename, bulk set date, bulk set notes. The bulk command pattern is already in place; new fields slot into the existing tri-state UI.
- **Document grouping / scoping** via the existing `GroupId` field. Reserved for future use; the column already exists on the document, the filter is already plumbed through the query, but no UI surfaces it in v1.
- **A user-facing field-level change-history view** beyond the activity feed — a per-field diff surface sourced from the existing audit log. No backfill required.
- **In-document preview, annotation, version diffs, version revert.**
- **Document-to-document relationships** (supersedes, derived-from, etc.) beyond the party link.
- **Additional AI providers** (Anthropic Claude, OpenAI, Ollama) as new `AiProviderType` enum values and config records — the `IChatClient` abstraction and factory pattern make this additive.
- **New AI-suggested fields** (document date, parties, expiry date) via new `DocumentSuggestionField` enum values, prompt updates, and UI — no schema changes needed.
- **AI-driven tag creation** — v1 limits AI to picking from existing tags; suggesting new tags requires a new prompt strategy and a creation flow.
- **External-source ingestion** — cloud-storage import, scanner uploads, email forwarding.
- **Multi-user / per-document permissions** if the household ever expands beyond a single trust boundary.

None of these should require re-migrating historical document or version data.
