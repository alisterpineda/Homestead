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
- A one-way **backup mirror** that reproduces every document version onto a configured destination — a separate filesystem root (local disk, NAS share, bind-mounted volume) or Google Drive — as human-named files in a year-sharded, browsable tree under its own `Documents/` folder, usable with no Homestead running. Instance-wide, files-only, all versions, mirror-deletes behind a bulk-delete circuit breaker. Opt-in: an admin pairs the documents source kind with a configured target at `/admin/backup`. An admin-only, read-only **Backup** section on a document's Metadata tab reports where that document stands in each mirror. See [backup.md](backup.md).
- A configured default text-extraction engine, with an optional per-run override on reprocess and a built-in fallback. Engines are *defined and chosen* in deployment config (`Ocr:Engines:*` — local Tesseract always, Azure Document Intelligence when configured — plus `Ocr:DefaultEngine`); the Documents settings page reports them read-only. Every extraction records which engine produced the text.
- AI-powered document classification and summarization: a configured LLM classifies documents by selecting matching tags, linking matching parties, suggesting a title, and reading off the document date and expiry date — reading both the extracted text and a rendered image of the first page — and, as a separate action, generates a document summary. Classification is **field-scoped** — the user picks which of tags, parties, name, document date and expiry date to (re)classify, and the prompt is built only for those fields. Tags, parties, name and the two dates are tracked via a generic suggestion system with per-field approve/decline; the summary is written directly without a suggestion record (no approve/decline — purely AI-managed). Both classify and summarize run on a single document or in bulk, on the background-job path.
- An optional **MCP server** through which a user's own Claude client (claude.ai, Claude Desktop, Claude Code) searches, edits metadata, resolves AI suggestions, comments on and trashes documents as that user — same roles, same access rules, same audit trail as the web app. No upload, restore or permanent delete. See [shared/mcp.md](../shared/mcp.md).

## Out of scope for v1

- Configurable retention policies and auto-purge — both trash and version history accumulate indefinitely. (Planned, see *Future-compatibility*.)
- Editing or deleting comments — comments are immutable in v1. (Planned, see *Future-compatibility*.)
- Bulk-editing fields beyond tags and parties (no bulk rename, bulk-set date, bulk-set notes). (Planned, see *Future-compatibility*.)
- Document grouping / scoping beyond a single household — the `GroupId` field is live server-side (accepted and validated on create/patch, plumbed through endpoints and filters) and the detail page shows a read-only group badge, but there is no group-assignment UI in v1. Richer scoping is reserved for future use, not necessarily multi-household.
- Two-way sync with the backup mirror, restore tooling, and per-document / per-tag backup selection — the mirror is strictly one-way and instance-wide. Cloud targets were out of scope for v1 but have since shipped (Google Drive). See [backup.md](backup.md).
- In-document collaborative editing, signing flows, approval workflows.
- Inline preview / annotation beyond a thumbnail.
- Version diffs and version revert. (Version *delete* is supported — see [versions.md](versions.md) → *Version delete*.)
- Document-to-document relationships (supersedes / derived-from / etc.) beyond the party link.
- Automated *creation* of new parties from document content — AI classification matches only parties that already exist; it never invents new ones. (A user's own Claude client, connected through the [MCP server](../shared/mcp.md), can create a tag or party — but only as that user, on their explicit ask, never as a side effect of classification.)
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

Documents can be classified and summarized by an LLM — the Claude API, via one operator-configured key and model. These are **two distinct actions** with separate triggers:

- **Classify** populates the organisational and identity fields: matching **tags**, linked **parties**, a suggested **name**, and the **document date** and **expiry date**. Classification is **field-scoped** — the user chooses which of those five fields to (re)classify, and the prompt is built dynamically for only the selected fields, so "just refresh the tags" never overwrites a curated name and never spends tokens producing outputs that get discarded. As with tags, the AI links only parties that already exist; it never creates new ones. That rule is the built-in classifier's: the user's own Claude client, over the [MCP server](../shared/mcp.md), can create a tag or party when the user asks it to — an ordinary write by that user, attributed to them like any other.
  - The **document date** is when the document was issued or written — an invoice date, letter date, or statement closing date; never a due date, a transaction date, or a printed/received date. The **expiry date** is when the document itself stops being valid — an ID or licence expiry, the end of a policy or coverage period, a warranty, or a contract term — and only when explicitly stated; a payment due date is not an expiry. The model returns a date only when it is stated and unambiguous (`03/04/2025` with no locale cue is left blank, never guessed); the server drops anything that isn't a plausible ISO date.
  - Dates **overwrite like the name**: an AI date that differs from the original replaces it, and the suggestion records the original — which may be empty — so a decline restores it exactly. "The original" is the user's value: the current date when no AI suggestion is pending, else the one the pending suggestion recorded. An AI answer of "no date" never clears a user's date; on a re-classify it reverts a still-pending AI date to its original (which may be empty), as an answer equal to the original does. An unchanged date records no suggestion.
  - The **name** follows one house style — see [Suggested title style](#suggested-title-style).
- **Summarize** generates the document's **summary** and nothing else. It is deliberately separate so a user who has curated tags, parties, and name can regenerate the summary — e.g. after switching to a better model or re-running OCR — without disturbing anything else. The summary is written **directly**: no suggestion record, no approve/decline, rendered as read-only text. It is purely AI-managed, and "no undo" is acceptable because regenerating it is a single deliberate action.

#### Suggested title style

The name is not just a list label: it is the highest-weighted search field and the filename in the backup mirror and ZIP download, and the mirror is meant to be browsable without the database. So a suggested title must **stand on its own** — it is built from the document (and its current name) alone, never from the tag or party catalogs or links, and reads the same whichever other fields are in scope.

**Shape:** `Issuer Type - Detail - Period`, in Title Case, aiming for 60 characters or fewer, parts separated by a plain ASCII ` - ` (typeable in search, safe on every filesystem).

| Part | Rule |
|---|---|
| **Issuer** | Always present. The most prominent printed name (logo or letterhead), Title-Cased if printed in capitals, legal suffixes (Ltd., Inc., Corp.) dropped; spelling and abbreviation as printed. |
| **Type** | Prefer a short vocabulary — Invoice, Receipt, Bill, Statement, Pay Stub, Policy, Notice of Assessment, Tax Return, Contract, Warranty, Manual, Letter, Licence — in Canadian spelling, with a free fallback when none fits, so a search for "invoice" finds them all. |
| **Detail** | One phrase saying what it's for — `Toyota Highlander Tire Changeover`, not `Tire Changeover - Toyota Highlander`. On a document about one person (ID, pay stub, medical), the person's full name; a document shared by several people names no one. An invoice/order number only when there is no clear subject. |
| **Period** | Only when the document **covers a stated period** — billing, statement, pay or policy period, tax year — at the document's own granularity (`March 2025`, `2024`, `2025–2026`, `Mar 1–15, 2025`). One-off documents carry no date. |

- **Why "covers a period" rather than "is recurring":** the model can't see the rest of the library, so it can't know a document belongs to a series — but a stated period is on the page. It is exactly what tells twelve monthly bills apart, which would otherwise collide in the list and in a mirror year folder. One-off documents already have the document date and the year shard.
- **No extra parts:** ` - ` always means "next field", so the last part is always the detail or the period. Splitting the detail into sub-parts would make a title's shape unpredictable and its last part ambiguous. The only multi-part detail is the person-plus-period shape of a personal document (`Acme Pay Stub - Jane Doe - March 2025`).
- **Over budget:** 60 characters is a recommendation, not a cap. A title that runs long shortens the detail first and never drops the issuer, type, or period.
- **No sensitive identifiers:** never full or partial (last-4) account, card, policy, SIN, or licence numbers — titles land in filenames on external storage.
- **Language:** English, proper nouns as printed (`Hydro-Québec Bill - March 2025`).
- **Existing names are normalised:** the AI rewrites a descriptive name into the house style too — it is a pending suggestion, so decline restores the original.
- **Unidentifiable documents keep their name:** when the model can't tell what a document is, it returns the current name, which records no suggestion — a vague "Scanned Document" is no better than the filename and is only noise to review.

Examples: `Kal Tire Invoice - Winter Tire Install` · `Hydro One Bill - March 2025` · `Acme Pay Stub - Jane Doe - March 2025` · `TD Joint Chequing Statement - March 2025` · `Canada Revenue Agency Notice of Assessment - 2024` · `Amazon Order - #702-1234567`.

Dropping the issuer was considered and rejected: same-month bills from different utilities, or two credit cards, become indistinguishable (identifiers are excluded), the mirror's filenames stop saying who a document is from, and an image-only scan would be searchable by issuer only through a party link. The ZIP download still appends party names, so issuers often appear twice there — a known follow-up, left as is for now.

**Image-augmented classification.** Beyond the extracted OCR text, classify also hands the model a **rendered image of the document's first page**. Letterheads, title blocks, and multi-column structure are exactly the cues OCR flattens, and they matter most for getting the title, type, and parties right — a scanned page with no text layer can be classified from the image alone. The image is attached only when the scope includes **name, parties, a date, or summary** — dates sit in headers and footers OCR garbles (a tags-only refresh leans on the text and tag catalog and skips the slower vision call); it is a dedicated, cached first-page render, separate from the display thumbnail. Image input is on by default wherever AI is configured, with a config kill-switch — a cost knob, since the image is most of a classify's input tokens. Because the vision step adds ~15–30s, every image-carrying classify runs on the background-job path.

**Native PDF input.** For a PDF, classify can instead hand the model **the PDF itself**: Claude reads the text and layout of every page, where the default path sees a head-and-tail sample of the OCR text plus one rendered page. It is the larger quality lever for short household documents, and it is on by default. It applies only where the image would (name, parties, a date or summary in scope — never a tags-only refresh) and only on the background path, and only within gates that bound the cost: a page cap (pages drive tokens), a size cap, and no encryption. In this mode the PDF **replaces** the OCR text — the operator's OCR-engine choice (language packs, handwriting, table markup) is bypassed for classification — while the name and the tag/party catalogs are still sent as text. A PDF outside the gates takes the OCR text + page-image path, and a PDF the model's API rejects is retried once on that path, so the mode never makes a classify fail that would otherwise have worked. One config switch turns it off. The PDF reaches the model as a short-lived upload to Anthropic's file storage, deleted as soon as the call ends (and expiring within the hour if that delete is missed), rather than inside the request. Statement parsing stays on the OCR text + page image.

Both actions run on a **single document** (from the detail page) or in **bulk** (from the list's selection mode), each via a small dialog; the classify dialog adds the per-field checkboxes.

**Classify results are auto-applied and then reviewed — the same way on every surface.** Single-document classify (detail page), bulk classify (list selection mode), and auto-classify-on-upload all run as **background jobs** that apply the AI's picks immediately, so the values are useful right away, recording each applied field as a pending **suggestion** to review afterwards. Running in the background is required, not just preferred: the image-augmented vision step (~15–30s) is too slow to hold a request open. On the detail page the action shows a progress toast during the run and, on completion, the document refreshes to show the applied values with their pending-suggestion controls inline. There is no separate per-document review step — review happens through those controls, right where each value is displayed. (This unified single-doc behaviour with bulk and upload; single-document classify is no longer a save-to-commit transient preview.)

The suggestion system is generic — `DocumentSuggestion` tracks AI-applied fields (Tags, Parties, Name, DocumentDate, ExpiryDate) with `Pending → Approved | Declined` status. Approving confirms the AI value (it stays applied; the AI indicator disappears). Declining reverts it: the AI-applied tag or party link is removed, or the name or date reverts to its original value (a date whose original was empty is cleared). Approve-all and decline-all are available when multiple suggestions are pending. Re-classifying clears prior pending suggestions **only for the fields in scope** before applying new ones — refreshing tags leaves a pending name suggestion untouched. A manual edit to a field auto-declines that field's pending suggestion — the user has taken explicit ownership.

**Triggers.** *Classify* — manually from the detail page, as a bulk action from the list, or automatically on upload when the household-wide `AutoClassifyOnUpload` setting is enabled and AI is configured. Auto-classify on upload runs at **full scope** (all fields plus summary), since a freshly-uploaded document has nothing curated to protect. It runs as part of the upload's background processing and is non-fatal — a classification failure never fails the upload — and the upload's completion is held until classification finishes, so the document surfaces already tagged, named, and summarized. *Summarize* — manually from the detail page or as a bulk action.

AI is a single, optional integration configured **outside the app** by the operator — a Claude API key and model (plus optional effort, refusal-fallback and input settings) supplied via environment/secrets. There is no in-app provider CRUD and no per-document model choice: when those settings are present the feature lights up, and when they are absent it degrades to a graceful no-op. Admin surfaces it **read-only** at `/admin/ai` — one status card (configured / unavailable with the reason / not configured; model, endpoint override, key-present, effort, refusal fallback, input settings) plus a test-connection action. Config that is present but can't run reads as unavailable-with-reason rather than a silent no-op — including the retired multi-provider keys, which are detected and named. The Documents settings page (`/documents/settings`) carries only the auto-classify toggle. The classification system is still built to extend: adding a new AI-suggested field needs no schema change (parties and the two dates were added this way) — but it is more than an enum value: the scope flag, prompt and response schema, apply, revert, the manual-edit auto-decline, and the UI all take a line each. The checklist lives on `DocumentSuggestionField`, and a unit test fails when a field is missing from the scope mapping, the reverter or the auto-decline.

### Trash

Deleting a document soft-deletes it: the document gains a `DeletedAt` timestamp and disappears from the default list. Trash has a dedicated route (`/documents/trash`) that the user must navigate to explicitly. From Trash, a document can be **restored** (clears `DeletedAt`) or **permanently deleted** (removes the row and every version's file from disk). An "Empty Trash" affordance permanently deletes everything in Trash.

Trash retention is unbounded in v1 — nothing auto-purges. Configurable retention is planned. Soft-delete via Trash applies to documents only; tags, parties, comments, and individual versions delete destructively.

Permanent delete also removes any **Finances transaction-document links** citing the document — via the DB `ON DELETE CASCADE` on the link table's cross-schema FK, the same mechanism that removes the document's versions, tags, and parties (see [finances/transactions.md § Linked documents](../finances/transactions.md#linked-documents)). Trashing does **not** touch them: Finances renders links to trashed documents greyed, and restore revives them unchanged.

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
8. **AI suggestions integrate inline**, not as a separate panel or modal. The name input and the document-date and expiry-date pickers gain an AI badge in their label, approve/decline buttons inside the input (in place of the date picker's clear button), and an "Original: …" hover tooltip ("(none)" when the field was empty); AI-suggested tags and parties render inside their own combobox with a dashed border and sparkle icon with per-item approve/decline; the summary appears as plain read-only text (no approve/decline — it is purely AI-managed). There is no "review suggestions" step — the values are already applied and the approve/decline controls are right where the values are displayed.
9. **AI-suggested tags and parties are visually distinct** — dashed border, sparkle icon, and per-item check/X buttons — so the user can tell at a glance which tags/parties came from AI vs manual assignment.
10. **Approve-all / decline-all** appears as a compact inline action row when two or more suggestions are pending, providing a fast path for users who trust or distrust the classification wholesale.
11. **Classify is field-scoped; summarize is its own action.** The classify dialog lets the user tick exactly which of tags, parties, name, document date and expiry date to (re)classify (all ticked by default), so refreshing one field never overwrites the others — and the summary sits behind a separate "Summarize" button. Two deliberate, narrowly-scoped AI actions instead of one all-or-nothing "classify everything" button.

## Future-compatibility

The following must be addable later without re-migrating documents, versions, tags, or audit history:

- **Configurable retention** for trash, old versions, and the audit log. The current "indefinite" stance lets retention be added as a policy applied at read/cleanup time without changing the data shape.
- **Editable / deletable comments.** Comments are persisted with author and timestamps; adding edit/delete is a UI and endpoint concern, not a data migration.
- **Wider bulk-edit surface** — bulk rename, bulk set date, bulk set notes. The bulk command pattern is already in place; new fields slot into the existing tri-state UI.
- **Document grouping / scoping** via the existing `GroupId` field. The column, create/patch validation, query filter, and a read-only group badge on the detail page already ship; what remains future is the group-assignment UI and any richer scoping semantics.
- **A user-facing field-level change-history view** beyond the activity feed — a per-field diff surface sourced from the existing audit log. No backfill required.
- **In-document preview, annotation, version diffs, version revert.**
- **Document-to-document relationships** (supersedes, derived-from, etc.) beyond the party link.
- **Another AI backend.** A second implementation of the single `IAiProvider` seam, selected by config; prompts, schemas and reply DTOs are backend-neutral data, so they move over unchanged.
- **New AI-suggested fields** via new `DocumentSuggestionField` enum values and the code touch-points listed on that enum — no schema changes needed (tags, name, parties and the two dates already use this).
- **AI-driven tag creation** — v1 limits the built-in classifier to picking from existing tags; suggesting new tags requires a new prompt strategy and a creation flow. (Creating a tag through the user's own Claude client over MCP, on the user's explicit ask, already ships — see [shared/mcp.md](../shared/mcp.md); it is a user write, not classifier output.)
- **External-source ingestion** — cloud-storage import, scanner uploads, email forwarding.
- **Multi-user / per-document permissions** if the household ever expands beyond a single trust boundary.

None of these should require re-migrating historical document or version data.
