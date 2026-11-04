# Versions and text extraction — design

This file covers the version model — when to add a version vs create a new document, what each upload produces, the document-scope vs version-scope split, the asynchronous upload pipeline, the text-extraction contract, and the household-wide engine setting. The high-level picture lives in [overview.md](overview.md); load this when working on uploads, the version list, reprocessing, or the extraction-engine settings page.

## When to add a version vs create a new document

A new **version** is the same artifact evolving — the signed copy of a draft, an annotated lease, a contract with an addendum. The bytes change but the *thing* is still the same physical or legal artifact, and document-scoped metadata (name, tags, parties, dates) carries through unchanged.

A new **document** is a new artifact, even if it concerns the same subject. A renewed passport is a new document, not a new version of the prior one — the prior passport remains a distinct, immutable artifact in its own right. Likewise a replacement lease that supersedes a prior one (rather than amending it), or a re-issued statement that corrects an earlier one.

The test: would the user ever want to refer to the older state as a self-contained artifact rather than as history of the current one? If yes, it's a new document. If the older state is only ever interesting as the predecessor of the current one, it's a prior version.

Multiple parallel representations of the same artifact at the same point in time (digital + scanned, colour + greyscale of one document) are not versioning either — versioning is a lifecycle, not a set of formats. Multi-format support is out of scope; if it ever becomes needed, it should be modelled as a separate concept, not by widening versioning to cover it.

## Version model

Every upload to a document produces a new `DocumentVersion` row. Versions are numbered monotonically per document — the first upload is `VersionNumber = 1`, the next `2`, and so on. Later uploads do not replace or rewrite earlier versions; the earlier rows persist alongside the new one, with their file blobs and `FileHash` immutable. Older versions can be explicitly deleted by the user (see *Version delete*) but accumulate by default.

A version carries:

- `VersionNumber` — assigned by the system, immutable.
- `Label` — an optional, user-editable string ("v1 — initial," "post-correction," etc.).
- File metadata: `ContentType`, `FileSize`, `FileExtension`, `FilePath` (computed), `FileHash` (SHA-256, globally unique).
- `ExtractedText` — populated asynchronously after upload; rewritten by reprocess.
- `CreatedAt`, `UpdatedAt`.

A document's "current" content is its highest-numbered version. Search, thumbnails, ZIP downloads, and detail-page previews all use the latest version. Older versions are listed on the detail page and individually downloadable.

## Document scope vs version scope

The split between document-scoped and version-scoped fields is a contract:

| Scope | Fields |
|---|---|
| Document | Name, Notes, DocumentDate, ExpiryDate, Summary, Tags, Parties, Suggestions, `GroupId`, trash state |
| Version | File blob, ContentType, FileSize, FileExtension, FileHash, ExtractedText, Label |

Document-scoped fields persist across uploads — replacing a file with a corrected version does not reset the document's name, notes, dates, or tag/party graph. Version-scoped fields belong to the specific upload that produced them; reprocessing a version updates only that version's `ExtractedText`.

Patches respect the split. The document patch endpoint accepts only document-scoped fields; version-scoped fields (label, extracted text) are edited through a separate version-scoped endpoint.

## Upload pipeline

Document and version uploads share the same shape: the API stages the file, persists a row, and enqueues an asynchronous job to finish processing. The synchronous request returns once the row is persisted — the user sees the document on the list right away — but the file hash, thumbnail, and extracted text materialise once the job completes.

The job, in order:

1. Runs text extraction (best-effort — a failure logs a warning and the upload still proceeds without extracted text). See *Text extraction* below.
2. Computes the SHA-256 hash of the staged file. The hash is unique across the household (enforced by an index on `DocumentVersion.FileHash`); an upload that hashes to an existing version's hash is rejected as a duplicate. The duplicate check looks across **all** versions, including those belonging to soft-deleted documents in Trash.
3. Moves the file to its final hash-addressed location, persists the document/version row, and sends the completion notification to the user.
4. Generates a thumbnail where the file type allows — this runs **after** the completion notification and is best-effort.

The job never updates `Document.SearchVector` itself: the column is maintained by database triggers (see [search.md](search.md#weighting)), so persisting the new name and version content refreshes the FTS index automatically.

Processing failures do not roll the upload back: the file remains, the row remains, and downstream artefacts (thumbnail, extracted text) may be missing. The user can reprocess to retry.

When `AutoClassifyOnUpload` is enabled in the Documents settings and AI is configured, the upload job runs classification **in-memory, before it reports completion** — it is not a separate `ClassifyDocument` job. After extracting text, the job renders the first-page image and calls the classifier over the document name, extracted text, and that image (full `All` scope), then folds the result into the document-creation command, so the document is created with its AI-suggested name, tags, parties, and summary already applied — a single `Created` audit entry, no suppressed `Updated`. Completion is therefore **held** until classification finishes (now ~15–30s when the page image is attached): the SignalR completion notification carries the AI-suggested name, so the user sees the final name as soon as the document appears in the list. Classification is non-fatal — if it fails (or AI is not configured), the failure is logged and the upload still completes with the user-supplied name. The standalone `ClassifyDocument` job type is used by bulk classification — and, since the interactive single-document classify now enqueues a one-item bulk job, by that path too. See the [overview](overview.md#ai-classification-and-summarization) for the classification flow.

Two job handlers cover the two entry points:

- `UploadDocumentJobHandler` — first upload of a brand-new document.
- `UploadDocumentVersionJobHandler` — subsequent versions on an existing document.

## Text extraction

Text extraction is asynchronous, best-effort, and engine-driven. After every upload the job invokes the configured extraction engine on the file and writes the result to `DocumentVersion.ExtractedText`.

**Failure contract:**

- **On upload, failures are silent.** The version is created with `ExtractedText = null`, a warning is logged server-side, and the upload completes successfully. The document is findable by name, tags, parties, and date, but is not full-text searchable until reprocessed. There is no v1 UI surface indicating that extraction failed for a given version; the user discovers the gap by searching for content and not finding it, then reprocesses.
- **On reprocess, failures surface inline.** A reprocess request that fails the extraction step throws; the version's existing `ExtractedText` is **not** overwritten, so a previously successful extraction is preserved across a failed retry. The user gets an error in the UI.

The asymmetry is deliberate. Uploads prioritise capturing the file safely; reprocess is an explicit user action and must report whether it took effect.

## Reprocess

Two reprocess paths:

- **Single-version reprocess.** Reruns extraction against a specific version's file. Updates the version's `ExtractedText`, refreshes the document's `SearchVector`, and surfaces failures inline. It may optionally pin a specific extraction engine for that one run — the reprocess dialog exposes an engine selector (`ReprocessDocumentVersionBody.EngineKey`); when none is chosen it uses the configured default (`Ocr:DefaultEngine`).
- **Bulk reprocess.** Enqueues a parent job that fans out to one child reprocess job per selected document, targeting each document's *latest* version. Older versions of a selected document are not reprocessed in bulk. Optionally pins an extraction engine for the entire run; otherwise the configured default is used. Progress is observable through the standard processing UI; the user is not blocked on completion.

Bulk reprocess is the recovery path when the extraction engine has been swapped, the previous engine had a defect, or a batch of uploads failed extraction silently.

## Text-extraction engine

Both facts about engines are deployment configuration, not data:

- **Which engines exist** is defined in config under `Ocr:Engines:{key}`, where `{key}` is an arbitrary instance name. Endpoints and API keys are deployment facts that belong in env vars / user-secrets, where the existing secret tooling already works — so there is no engines table, no admin CRUD, no secret masking, and no API keys in a `pg_dump`.
- **Which engine to use** is also configuration: `Ocr:DefaultEngine` names the default (a config instance key, e.g. `azure-layout`). The Documents settings page reports the engine list and the default **read-only**; a per-run override can still be pinned on either reprocess path.

Engines are *named instances* selected by string key, not one-per-type: a single Azure resource serves several meaningfully different models, so `azure-layout` (`prebuilt-layout`) and `azure-read` (`prebuilt-read`) are both legitimate engines over the same credentials.

Three engine types ship:

- **`tesseract`** — local, free, always present. For PDFs it takes a hybrid path: pages with an embedded text layer are read directly; only text-less pages are rasterized and OCR'd. The text layer is read with Poppler's `pdftotext -layout`, run once per document, so a statement's columns stay apart instead of being flattened into prose. The stored text is that output **compacted**: every run of two or more spaces becomes exactly two and trailing whitespace is dropped, so a row stays on one line and two spaces still marks a column break, but exact monospace alignment is deliberately not kept — raw `-layout` padding inflated text 1.1–2.3×, which would have pushed long statements past the AI input budgets and bloated MCP responses. When `pdftotext` is missing, fails, or reports a different page count than the PDF has, the whole document falls back to PdfPig's text layer (columns flattened) with a warning; provenance records which extractor ran.
- **Docling** — local, free, off unless configured. Calls a `docling-serve` sidecar (a separate container, MIT-licensed, fully offline) over its async submit-and-poll API with the whole file, and stores the Markdown it returns, with **pipe tables**. It does layout analysis and table-structure recognition, which is what Poppler's column-preserving text cannot: Poppler keeps columns apart on the line, but a table is still just aligned text, with no cells, headers or spans. It is slow on CPU (seconds to tens of seconds per page), so a Docling instance should set its own `JobTimeout` (see *Availability and failure*); every conversion is sent the extraction deadline as docling-serve's `document_timeout`, so no conversion runs unbounded. docling-serve cannot cancel a task, so each instance caps the conversions it has in flight (`MaxConcurrency`, default 1, matched to the sidecar's workers): further jobs wait in Homestead, where a job whose deadline passes simply gives up without submitting, instead of leaving a task queued in the sidecar to convert after its job is gone. A job that gets its slot late is sent only what is left of its limit. Docling converts PDFs and PNG/JPEG/TIFF/BMP images; any other file type records `unsupported_content_type` and stores no text.
- **Azure Document Intelligence** — metered per page, off unless configured. Sends the whole file regardless of text layer. With `prebuilt-layout` it returns Markdown containing **HTML table syntax** (`<table>/<tr>/<td>`, `rowspan`/`colspan` preserved). It predates Docling and was the first engine to give born-digital, table-heavy documents (bank statements) real table structure; it remains for deployments that prefer a hosted service.

A built-in `tesseract` instance always exists, seeded with defaults even when config defines no engines at all. It is the terminus of the resolution chain: **explicit per-run key → `Ocr:DefaultEngine` → built-in `tesseract`**. There is no sole-engine auto-pick and no per-user override.

### Availability and failure

An engine can be *configured as an instance* yet not *usable* (e.g. an Azure instance with no endpoint, or `prebuilt-read` asked for Markdown, which Azure rejects). Each engine reports `IsConfigured` plus a `ConfigurationError`, and the picker (`GET /api/v1/documents/text-extraction-engines`, Contributor) shows unusable engines disabled with the reason rather than hiding them. Config is not validated at startup, so an invalid combination surfaces in the picker rather than preventing the app from booting.

Failure handling is deliberately asymmetric:

- **On the default path** (uploads — nobody is watching), an unavailable or dangling engine key falls back to the built-in `tesseract`, logs, and records the substitution in the version's provenance metadata. The fall-back is never silent.
- **On an explicit pick**, an unknown or unavailable key is rejected with a **400 at enqueue**, before a job row exists. Silently running Tesseract for a user who chose `azure-layout` would deliver precisely the output they rejected.

Statement import sits on **neither** path — it does not extract at all. It consumes whatever `ExtractedText` a version already carries, and a version without any is rejected at initiate with a 400 (before a job row exists), pointing the user at reprocess. It was previously counted as a default path, but it never fit: an import is a blocking, user-initiated action with somebody watching a spinner, which the asymmetry above places on the loud side. It also never honoured the default path's provenance promise — its re-extract discarded its own result, writing neither text nor metadata. See [Statement import](../finances/transactions.md#statement-import).

**Slow engines degrade, they do not time the job out.** Each engine instance may set its own budget, `Ocr:Engines:{key}:JobTimeout` (else the global `Processing:JobTimeout`, 5 minutes); the OCR jobs run under the budget of the engine they will run. Extraction itself is cut off 30 s short of that budget (never more than half of it), leaving the rest of the job room to finish, and the cut-off is recorded as an extraction failure naming the engine and the deadline. So an upload whose extraction is too slow still creates the document — without text, like any extraction failure — and a reprocess fails with `text_extraction_failed` instead of a job timeout. An upload's auto-classify is bounded the same way: it gets what is left of the budget less a 10 s persist reserve (at most a tenth of the budget), and running out — or having nothing left to start with — creates the document unclassified rather than timing the job out. The extraction deadline holds even for a step that ignores cancellation (PdfPig parsing and PDF rasterization are synchronous): the engine runs on the thread pool and the job stops waiting for it. The accepted cost: an abandoned step keeps running until its next cancellation check while the job frees its worker slot, so a burst of pathological PDFs can briefly run more heavy steps than `Processing:MaxConcurrency`. Uploads are authenticated household users, and each abandonment costs one step and is logged, so there is no cap on abandoned work.

The engine is resolved **once, at enqueue**, and the concrete key is stamped into the job payload; a bulk fan-out copies the parent's key into every child. This makes a run deterministic — changing `Ocr:DefaultEngine` between deploys mid-run cannot split a batch across two engines — and makes the 400-at-enqueue possible at all.

### Provenance

Every extraction records how it was produced, on `DocumentVersion.TextExtractionMetadata` (jsonb), surfaced read-only under the extracted text. It carries the engine type, the instance key that ran, when, and — only when a fall-back happened — the key that was requested and why it could not run. Per type: Tesseract records language/DPI/PSM/OEM plus **how many pages came from the embedded text layer versus were OCR'd** (which branch of the hybrid path each page took is otherwise unknowable after the fact) and **which text-layer extractor ran** (`poppler`, or `pdfpig` after a fall-back; null for an image, and for versions extracted before the field existed); Docling records the server version (when the sidecar reports it), the table mode and force-OCR flag sent, the source page count (counted locally — the Markdown carries none), the processing time, and the **conversion status** — `partial_success` is the durable signal that some pages or tables failed and the text is incomplete; Azure records the model, output format, and **how many pages it actually analyzed** — the only durable signal that a free-tier (F0) resource silently analyzed just the first 2 pages of a 30-page statement.

**Provenance travels with the text it describes**: any API response that returns `ExtractedText` returns the provenance beside it. Concretely, both the version-detail endpoint and the document endpoint (whose latest-version text feeds the default "Latest" view) carry it — so the detail page never has to fetch text and its provenance separately, and the two can never describe different extractions. A new endpoint that exposes extracted text inherits this rule.

Versions extracted before provenance existed simply show nothing.

## Version delete

A user can delete an individual version of a document from the document detail page. The user selects the version to act on via the breadcrumb version dropdown, then chooses **Delete → This version** from the detail-page actions (⋮) menu; for a single-version document that menu offers a plain document delete instead. The delete is destructive — the row is removed, the file is deleted from disk, an audit entry is recorded, and the document's `SearchVector` is refreshed if the deleted version was the latest (so search reflects the new latest version's extracted text).

The **only-remaining-version guard**: deleting the last surviving version of a document is rejected. To get rid of a document entirely, soft-delete the document itself (which moves it to Trash) and then permanently delete it from there — that path removes every version's file along with the document.

Version delete is the user's recovery path for "I uploaded the wrong intermediate state" — for example, a botched scan of a form that was followed by a clean scan, or an addendum uploaded against the wrong document. Reprocess does not remove a row; only explicit version delete does.

Older versions accumulate by default; nothing auto-prunes. When a document is permanently deleted from Trash, all of its versions and their files go with it regardless of how many there are.

Configurable retention (auto-prune old versions, cap version count per document, etc.) is a v2 concern.
