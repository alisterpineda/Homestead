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
| Document | Name, Notes, DocumentDate, ExpiryDate, Summary, Tags, Parties, Suggestions, `GroupId` (deferred), trash state |
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

When `AutoClassifyOnUpload` is enabled in the Documents settings and a default AI provider is configured and enabled, the upload job runs classification **in-memory, before it reports completion** — it is not a separate `ClassifyDocument` job. After extracting text, the job calls the classifier over the document name and extracted text (full `All` scope) and folds the result into the document-creation command, so the document is created with its AI-suggested name, tags, parties, and summary already applied — a single `Created` audit entry, no suppressed `Updated`. Completion is therefore **held** until classification finishes: the SignalR completion notification carries the AI-suggested name, so the user sees the final name as soon as the document appears in the list. Classification is non-fatal — if it fails (or no provider is configured), the failure is logged and the upload still completes with the user-supplied name. The standalone `ClassifyDocument` job type is used only by bulk classification. See the [overview](overview.md#ai-classification-and-summarization) for the classification flow.

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

- **Single-version reprocess.** Reruns extraction against a specific version's file. Updates the version's `ExtractedText`, refreshes the document's `SearchVector`, and surfaces failures inline. It may optionally pin a specific extraction engine for that one run — the reprocess dialog exposes an engine selector (`ReprocessDocumentVersionBody.TextExtractionEngineId`); when none is chosen it uses the household-wide default.
- **Bulk reprocess.** Enqueues a parent job that fans out to one child reprocess job per selected document, targeting each document's *latest* version. Older versions of a selected document are not reprocessed in bulk. Optionally pins an extraction engine for the entire run; otherwise each child uses the household-wide default. Progress is observable through the standard processing UI; the user is not blocked on completion.

Bulk reprocess is the recovery path when the extraction engine has been swapped, the previous engine had a defect, or a batch of uploads failed extraction silently.

## Text-extraction engine

The Documents module has a household-wide `DefaultTextExtractionEngineId` setting, persisted as a single row in `AppSettings` keyed by module name. This is the engine used for upload extraction and for reprocess actions that do not pin a specific engine. The setting is editable from the Documents settings page (`/documents/settings`); the dropdown lists every engine the system advertises.

A built-in fallback engine always exists and is used when no preference has been set. The setting is shared by the entire household — there is no per-user override.

Engines are an extensibility point at the model layer (new engines can be registered without touching the upload or reprocess paths), but in v1 the user-facing surface is intentionally small: a single household-wide preference, plus an optional one-shot engine override on a reprocess run — available on both single-version and bulk reprocess.

## Version delete

A user can delete an individual version of a document from the document detail page. The user selects the version to act on via the breadcrumb version dropdown, then chooses **Delete → This version** from the detail-page actions (⋮) menu; for a single-version document that menu offers a plain document delete instead. The delete is destructive — the row is removed, the file is deleted from disk, an audit entry is recorded, and the document's `SearchVector` is refreshed if the deleted version was the latest (so search reflects the new latest version's extracted text).

The **only-remaining-version guard**: deleting the last surviving version of a document is rejected. To get rid of a document entirely, soft-delete the document itself (which moves it to Trash) and then permanently delete it from there — that path removes every version's file along with the document.

Version delete is the user's recovery path for "I uploaded the wrong intermediate state" — for example, a botched scan of a form that was followed by a clean scan, or an addendum uploaded against the wrong document. Reprocess does not remove a row; only explicit version delete does.

Older versions accumulate by default; nothing auto-prunes. When a document is permanently deleted from Trash, all of its versions and their files go with it regardless of how many there are.

Configurable retention (auto-prune old versions, cap version count per document, etc.) is a v2 concern.
