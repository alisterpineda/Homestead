# Versions and text extraction — design

This file covers the version model — what each upload produces, the document-scope vs version-scope split, the asynchronous upload pipeline, the text-extraction contract, and the household-wide engine setting. The high-level picture lives in [overview.md](overview.md); load this when working on uploads, the version list, reprocessing, or the extraction-engine settings page.

## Version model

Every upload to a document produces a new `DocumentVersion` row. Versions are numbered monotonically per document — the first upload is `VersionNumber = 1`, the next `2`, and so on. Older versions are not deleted, replaced, or rewritten by later uploads; they remain accessible read-only.

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
| Document | Name, Notes, DocumentDate, ExpiryDate, Tags, Parties, `GroupId` (deferred), trash state |
| Version | File blob, ContentType, FileSize, FileExtension, FileHash, ExtractedText, Label |

Document-scoped fields persist across uploads — replacing a file with a corrected version does not reset the document's name, notes, dates, or tag/party graph. Version-scoped fields belong to the specific upload that produced them; reprocessing a version updates only that version's `ExtractedText`.

Patches respect the split. The document patch endpoint accepts only document-scoped fields; version-scoped fields (label, extracted text) are edited through a separate version-scoped endpoint.

## Upload pipeline

Document and version uploads share the same shape: the API stages the file, persists a row, and enqueues an asynchronous job to finish processing. The synchronous request returns once the row is persisted — the user sees the document on the list right away — but the file hash, thumbnail, and extracted text materialise once the job completes.

The job:

1. Computes the SHA-256 hash of the staged file. The hash is unique across the household (enforced by an index on `DocumentVersion.FileHash`); an upload that hashes to an existing version's hash is rejected as a duplicate. The duplicate check looks across **all** versions, including those belonging to soft-deleted documents in Trash.
2. Generates a thumbnail where the file type allows.
3. Runs text extraction (see *Text extraction* below).
4. Updates the document's `SearchVector` so the new content is reflected in FTS results.

Processing failures do not roll the upload back: the file remains, the row remains, and downstream artefacts (thumbnail, extracted text) may be missing. The user can reprocess to retry.

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

- **Single-version reprocess.** Reruns extraction against a specific version's file. Updates the version's `ExtractedText`, refreshes the document's `SearchVector`, and surfaces failures inline.
- **Bulk reprocess.** Enqueues a parent job that fans out to one child reprocess job per selected document, targeting each document's *latest* version. Older versions of a selected document are not reprocessed in bulk. Optionally pins an extraction engine for the entire run; otherwise each child uses the household-wide default. Progress is observable through the standard processing UI; the user is not blocked on completion.

Bulk reprocess is the recovery path when the extraction engine has been swapped, the previous engine had a defect, or a batch of uploads failed extraction silently.

## Text-extraction engine

The Documents module has a household-wide `DefaultTextExtractionEngineId` setting, persisted as a single row in `AppSettings` keyed by module name. This is the engine used for upload extraction and for reprocess actions that do not pin a specific engine. The setting is editable from the Documents settings page (`/documents/settings`); the dropdown lists every engine the system advertises.

A built-in fallback engine always exists and is used when no preference has been set. The setting is shared by the entire household — there is no per-user override.

Engines are an extensibility point at the model layer (new engines can be registered without touching the upload or reprocess paths), but in v1 the user-facing surface is intentionally small: a single household-wide preference, plus an optional one-shot override on a bulk reprocess run.

## Version delete and retention

There is no version delete in v1. Every upload accumulates a new row indefinitely; reprocess updates a row in place but never removes one. When a document is permanently deleted from Trash, all of its versions and their files go with it.

Configurable retention (auto-prune old versions, cap version count per document, etc.) is a v2 concern.
