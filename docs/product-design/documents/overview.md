# Documents — Requirements

This document describes the Documents module within Homestead: what it is, what it does, what it does not do, and the design decisions that shape it. It is product-level, not implementation-level — see the repo's CLAUDE.md files for code structure and framework choices.

Documents sits alongside Finances and the cross-cutting infrastructure modules listed in the [product-design index](../README.md). This file governs *what* the module should do; defer to existing module patterns in the codebase for *how* it is wired up.

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
- Bulk operations: bulk soft-delete, bulk metadata edit (tags and parties only), bulk reprocess, bulk classify, bulk summarize, bulk download.
- PostgreSQL full-text search across document name (weighted A), the latest version's extracted text (weighted B), and document summary (weighted C), with ILIKE fallbacks on name and extracted text, and prefix-expansion on user input. Zero-result searches surface fuzzy "Did you mean…?" suggestions over document, tag, and party names via `pg_trgm`.
- ZIP download organised by tag path, with a dedicated `_Untagged/` folder and party names embedded in filenames.
- Household-wide choice of text-extraction engine, with an optional per-run override on reprocess and a built-in fallback. Engines are *defined* in deployment config (local Tesseract always; Azure Document Intelligence when configured) but *chosen* as product data. Every extraction records which engine produced the text.
- AI-powered document classification and summarization: a configured LLM classifies documents by selecting matching tags, linking matching parties, and suggesting a title — reading both the extracted text and a rendered image of the first page — and, as a separate action, generates a document summary. Classification is **field-scoped** — the user picks which of tags, parties, and name to (re)classify, and the prompt is built only for those fields. Tags, parties, and name are tracked via a generic suggestion system with per-field approve/decline; the summary is written directly without a suggestion record (no approve/decline — purely AI-managed). Both classify and summarize run on a single document or in bulk, on the background-job path.

## Out of scope for v1

- Configurable retention policies and auto-purge — both trash and version history accumulate indefinitely. (Planned, see *Future-compatibility*.)
- Editing or deleting comments — comments are immutable in v1. (Planned, see *Future-compatibility*.)
- Bulk-editing fields beyond tags and parties (no bulk rename, bulk-set date, bulk-set notes). (Planned, see *Future-compatibility*.)
- Document grouping / scoping beyond a single household — the `GroupId` field is live server-side (accepted and validated on create/patch, plumbed through endpoints and filters) and the detail page shows a read-only group badge, but there is no group-assignment UI in v1. Richer scoping is reserved for future use, not necessarily multi-household.
- In-document collaborative editing, signing flows, approval workflows.
- Inline preview / annotation beyond a thumbnail.
- Version diffs and version revert. (Version *delete* is supported — see [versions.md](versions.md) → *Version delete*.)
- Document-to-document relationships (supersedes / derived-from / etc.) beyond the party link.
- Automated *creation* of new parties from document content — AI classification matches only parties that already exist; it never invents new ones.
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

- **Audit log entries** — system-generated, one per field-level change to the document or its versions (Name, DocumentDate, ExpiryDate, Notes, Summary, ExtractedText, TagPaths, PartyIds, Label). Recorded automatically on every mutation via the shared `auditing.audit_logs` table; field display names and human summaries are formatted at read time.
- **Comments** — user-authored free text. In v1 comments are immutable: they cannot be edited or deleted from the UI.

The activity stream is the user-facing projection of the audit log with comments interleaved. There is no separate field-level diff surface in v1.

### AI classification and summarization

Documents can be classified and summarized by a configured LLM — a single, operator-configured OpenAI-compatible endpoint. These are **two distinct actions** with separate triggers:

- **Classify** populates the organisational and identity fields: matching **tags**, linked **parties**, and a suggested **name**. Classification is **field-scoped** — the user chooses which of those three fields to (re)classify, and the prompt is built dynamically for only the selected fields, so "just refresh the tags" never overwrites a curated name and never spends tokens producing outputs that get discarded. As with tags, the AI links only parties that already exist; it never creates new ones.
- **Summarize** generates the document's **summary** and nothing else. It is deliberately separate so a user who has curated tags, parties, and name can regenerate the summary — e.g. after switching to a better model or re-running OCR — without disturbing anything else. The summary is written **directly**: no suggestion record, no approve/decline, rendered as read-only text. It is purely AI-managed, and "no undo" is acceptable because regenerating it is a single deliberate action.

**Image-augmented classification.** Beyond the extracted OCR text, classify also hands the model a **rendered image of the document's first page**. Letterheads, title blocks, and multi-column structure are exactly the cues OCR flattens, and they matter most for getting the title, type, and parties right — a scanned page with no text layer can be classified from the image alone. The image is attached only when the scope includes **name, parties, or summary** (a tags-only refresh leans on the text and tag catalog and skips the slower vision call); it is a dedicated, cached first-page render, separate from the display thumbnail. Image input is on by default wherever AI is configured, with a config kill-switch for endpoints whose model can't read images. Because the vision step adds ~15–30s, every image-carrying classify runs on the background-job path.

Both actions run on a **single document** (from the detail page) or in **bulk** (from the list's selection mode), each via a small dialog; the classify dialog adds the per-field checkboxes.

**Classify results are auto-applied and then reviewed — the same way on every surface.** Single-document classify (detail page), bulk classify (list selection mode), and auto-classify-on-upload all run as **background jobs** that apply the AI's picks immediately, so the values are useful right away, recording each applied field as a pending **suggestion** to review afterwards. Running in the background is required, not just preferred: the image-augmented vision step (~15–30s) is too slow to hold a request open. On the detail page the action shows a progress toast during the run and, on completion, the document refreshes to show the applied values with their pending-suggestion controls inline. There is no separate per-document review step — review happens through those controls, right where each value is displayed. (This unified single-doc behaviour with bulk and upload; single-document classify is no longer a save-to-commit transient preview.)

The suggestion system is generic — `DocumentSuggestion` tracks AI-applied fields (Tags, Parties, Name) with `Pending → Approved | Declined` status. Approving confirms the AI value (it stays applied; the AI indicator disappears). Declining reverts it: the AI-applied tag or party link is removed, or the name reverts to its original value. Approve-all and decline-all are available when multiple suggestions are pending. Re-classifying clears prior pending suggestions **only for the fields in scope** before applying new ones — refreshing tags leaves a pending name suggestion untouched. A manual edit to a field auto-declines that field's pending suggestion — the user has taken explicit ownership.

**Triggers.** *Classify* — manually from the detail page, as a bulk action from the list, or automatically on upload when the household-wide `AutoClassifyOnUpload` setting is enabled and AI is configured. Auto-classify on upload runs at **full scope** (all fields plus summary), since a freshly-uploaded document has nothing curated to protect. It runs as part of the upload's background processing and is non-fatal — a classification failure never fails the upload — and the upload's completion is held until classification finishes, so the document surfaces already tagged, named, and summarized. *Summarize* — manually from the detail page or as a bulk action.

AI is a single, optional integration configured **outside the app** by the operator — an OpenAI-compatible endpoint, model, and API key supplied via environment/secrets. There is no in-app provider CRUD and no per-document provider choice: when those settings are present the feature lights up, and when they are absent it degrades to a graceful no-op. Admin surfaces it **read-only** at `/admin/ai` — a connection status (configured? endpoint, model, key-present) plus a test-connection action. The Documents settings page (`/documents/settings`) carries only the auto-classify toggle. The classification system is still built to extend: adding a new AI-suggested field (document date, expiry date) needs only a new `DocumentSuggestionField` value, a prompt update, and UI — no schema change (parties were added this way).

### Trash

Deleting a document soft-deletes it: the document gains a `DeletedAt` timestamp and disappears from the default list. Trash has a dedicated route (`/documents/trash`) that the user must navigate to explicitly. From Trash, a document can be **restored** (clears `DeletedAt`) or **permanently deleted** (removes the row and every version's file from disk). An "Empty Trash" affordance permanently deletes everything in Trash.

Trash retention is unbounded in v1 — nothing auto-purges. Configurable retention is planned. Soft-delete via Trash applies to documents only; tags, parties, comments, and individual versions delete destructively.

### Search

The list page is search-first: a single text box at the top drives both content search and result ranking. Behind the scenes the query runs in five OR'd modes — case-insensitive ILIKE on document name, case-insensitive ILIKE on any version's extracted text, case-insensitive ILIKE on attached tag names, case-insensitive ILIKE on linked party names, and PostgreSQL full-text search via `ts_rank_cd` against a weighted `tsvector` (Name = A, latest version's ExtractedText = B, Summary = C), with prefix-expansion on user input. The tag and party branches federate the search bar with the filter popover so typing `invoice` finds documents tagged **Invoice** without opening the filter. When a search term is present, results are ordered by FTS rank; otherwise by the user's chosen sort field. Filters layer on top of search. When the primary search returns no results, the list surfaces "Did you mean…?" chips drawn from a `pg_trgm` fuzzy match against document, tag, and party names — opt-in, never silent.

See [search.md](search.md) for the query mechanics, weighting, prefix expansion, sort interaction, and the filter taxonomy.

### Bulk operations

The list page has a selection mode toggled inline (not a separate page). With one or more documents selected the user can:

- **Bulk soft-delete** to Trash.
- **Bulk metadata edit**: add or remove tags; add or remove parties.
- **Bulk reprocess** the latest version of each document, optionally pinning a specific extraction engine for the run.
- **Bulk classify** — choose which fields (tags, parties, name) to classify across the selected documents.
- **Bulk summarize** — (re)generate the summary for each selected document.
- **Bulk download** as a ZIP.

Bulk metadata edit uses tri-state controls — for each tag/party, the value can be "add to all," "remove from all," or "leave alone" — so existing per-document state is not overwritten unintentionally. Bulk metadata edit is intentionally narrow to taxonomy fields (tags, parties); document-level scalars (Name, Notes, DocumentDate, ExpiryDate) are not bulk-editable in v1.

### ZIP download

A bulk download enqueues a job that packages the selected documents into a single archive organised by tag path:

- A document tagged `Finances/Investments` is placed at `Finances/Investments/<filename>`.
- A document with multiple tags appears in **each** tag-path folder, with duplicated content. The duplication is intentional — each tag is an independent organisational view of the same document.
- A document with no tags is placed in a top-level `_Untagged/` folder.
- Linked party names are appended to the filename (`<docname> - <party1>, <party2><ext>`), truncated past 200 characters.
- Within any folder, filename collisions are resolved by appending a counter.

The job writes the resulting ZIP to a downloads area with a 7-day expiration; the client awaits a real-time SignalR completion notification (it registers the job with the processing hub and subscribes to its status — no polling) before downloading. Only one active download per user is permitted at a time.

## Design principles

### File preservation is non-negotiable; extraction is best-effort

Every accepted upload produces a Document and a Version regardless of whether text extraction succeeds. If OCR fails, the version lands with `ExtractedText = null` and a warning is logged server-side; the user sees a normal successful upload. The document remains findable by name, tags, parties, and date — it just isn't full-text searchable until reprocessed. Explicit reprocess actions, in contrast, surface failures inline so the user knows their request didn't take effect.

The asymmetry is deliberate: uploads prioritise capturing the file safely; reprocess prioritises feedback because the user asked for it.

### Document scope vs version scope is load-bearing

What belongs to the document and what belongs to the version is a contract, not an implementation detail:

- **Document-scoped:** Name, Notes, DocumentDate, ExpiryDate, Summary, Tags, Parties, Suggestions, GroupId, trash state.
- **Version-scoped:** the file blob, ContentType, FileSize, FileExtension, FileHash, ExtractedText, Label.

This split is what makes "uploading a corrected version of the same document" coherent: metadata you have already curated stays attached to the document, while file content rolls forward. Code paths and UI must respect the split — moving a field across the boundary is a deliberate decision, not an offhand patch.

### Tags are organisational, parties are semantic

Tags say where in the filing system a document lives. Parties say what entities the document concerns. Both are many-to-many with documents, but they answer different questions and the UI keeps them visually distinct. Search expands tags through their hierarchy (a query for `Finances` finds anything tagged `Finances/Investments/2024` when descendant expansion is on); parties match exactly.

### Versioning is monotonic

A new upload always produces a new version with the next version number. Older versions are not replaced or rewritten by later uploads — the earlier rows persist with their file blobs and hashes immutable. The user can download an older version and edit its `Label`, but cannot replace its content. There is no version revert and no version diff in v1.

Versions can be explicitly deleted by the user except for the only remaining version of a document (see [versions.md](versions.md) → *Version delete*). Delete is the recovery path for "I uploaded the wrong intermediate state."

### Activity is the audit log made visible

The activity stream is not a separate "what happened" log; it is a curated read of the existing field-level audit log, interleaved with user comments. There is exactly one source of truth for "what changed and when" — the audit log — and the activity feed is its user-facing projection. New auditable events show up in the feed automatically; registering a display name for a field just gives it a friendly label (an unregistered field falls back to its raw, lower-cased name), and nothing has to be double-written.

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
8. **AI suggestions integrate inline**, not as a separate panel or modal. The name input gains an AI badge with approve/decline buttons; AI-suggested tags and parties render inside their own combobox with a dashed border and sparkle icon with per-item approve/decline; the summary appears as plain read-only text (no approve/decline — it is purely AI-managed). There is no "review suggestions" step — the values are already applied and the approve/decline controls are right where the values are displayed.
9. **AI-suggested tags and parties are visually distinct** — dashed border, sparkle icon, and per-item check/X buttons — so the user can tell at a glance which tags/parties came from AI vs manual assignment.
10. **Approve-all / decline-all** appears as a compact inline action row when two or more suggestions are pending, providing a fast path for users who trust or distrust the classification wholesale.
11. **Classify is field-scoped; summarize is its own action.** The classify dialog lets the user tick exactly which of tags, parties, and name to (re)classify, so refreshing one field never overwrites the others — and the summary sits behind a separate "Summarize" button. Two deliberate, narrowly-scoped AI actions instead of one all-or-nothing "classify everything" button.

## Future-compatibility

The following must be addable later without re-migrating documents, versions, tags, or audit history:

- **Configurable retention** for trash, old versions, and the audit log. The current "indefinite" stance lets retention be added as a policy applied at read/cleanup time without changing the data shape.
- **Editable / deletable comments.** Comments are persisted with author and timestamps; adding edit/delete is a UI and endpoint concern, not a data migration.
- **Wider bulk-edit surface** — bulk rename, bulk set date, bulk set notes. The bulk command pattern is already in place; new fields slot into the existing tri-state UI.
- **Document grouping / scoping** via the existing `GroupId` field. The column, create/patch validation, query filter, and a read-only group badge on the detail page already ship; what remains future is the group-assignment UI and any richer scoping semantics.
- **A user-facing field-level change-history view** beyond the activity feed — a per-field diff surface sourced from the existing audit log. No backfill required.
- **In-document preview, annotation, version diffs, version revert.**
- **Document-to-document relationships** (supersedes, derived-from, etc.) beyond the party link.
- **Non-OpenAI-compatible AI backends.** Any OpenAI-compatible endpoint already works today purely by configuration (Azure OpenAI, OpenAI, a local Ollama / llama.cpp server, etc.) — there is no per-provider code path or enum. A backend that does not speak the OpenAI chat API would need a new client behind the `IChatClient` abstraction.
- **New AI-suggested fields** (document date, expiry date) via new `DocumentSuggestionField` enum values, prompt updates, and UI — no schema changes needed (tags, name, and parties already use this).
- **AI-driven tag creation** — v1 limits AI to picking from existing tags; suggesting new tags requires a new prompt strategy and a creation flow.
- **External-source ingestion** — cloud-storage import, scanner uploads, email forwarding.
- **Multi-user / per-document permissions** if the household ever expands beyond a single trust boundary.

None of these should require re-migrating historical document or version data.
