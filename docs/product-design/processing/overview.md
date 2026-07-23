# Processing — Architecture

This document describes Homestead's background-job processing subsystem: what it is, what it runs, and the design decisions that shape it. Processing is an **infrastructure** module — there is no user-facing product surface beyond a read-only admin job list, an admin schedules list, and SignalR-driven toasts. Most jobs today are initiated by another module (Documents and Finances) on the user's behalf; some can be issued by a recurring schedule (attributed to the schedule's creator). Every job runs asynchronously in the same WebApi process and reports completion back through SignalR (when it has a user creator).

This file lives under `docs/product-design/processing/` because it is consulted from product work in other modules — uploads, reprocessing, bulk operations, downloads, and any recurring work all flow through this queue, and adding new async work means extending it. Treat this doc as the source of truth for *what the queue guarantees and what it deliberately does not*; defer to the repo's CLAUDE.md files for code structure and framework choices.

## Purpose

Processing exists so user-initiated work that does not need to complete inline (text extraction, hashing, ZIP packaging, fan-out reprocessing) can be deferred to a background worker without losing the user's view of progress. The primary outcomes:

1. The HTTP request that initiates the work returns immediately with a job ID.
2. The work runs durably — surviving process restarts — against state stored in the application database.
3. The user receives a real-time status update when the job finishes (success, failure, or partial completion), without polling.
4. Failures and progress are visible after the fact in an admin job list.

The module is a **job-execution engine** plus a **cron-style schedule emitter** that feeds it. It is not a workflow engine, has no DAG semantics, and does not run user-defined scripts.

## In scope for v1

- A single durable, FIFO-with-priority queue stored in PostgreSQL (`processing.jobs` table).
- One in-process worker (the WebApi itself) that claims pending jobs, runs them with bounded concurrency, and writes their terminal status back to the database.
- A small, closed set of job **types** known to the worker, each backed by a handler class (see *Job catalog*).
- A two-level **parent/child** hierarchy for fan-out work, with the parent's status derived from the aggregate of its children.
- A separate **schedule emitter** (`processing.schedules` table + `JobSchedulerService` background tick loop) that turns cron expressions into Pending jobs in the same `processing.jobs` table. Schedules carry a nullable `CreatedByUserId` — both current creation endpoints stamp the creating admin; a creator-less (system-attributed) schedule is schema-supported but not yet produced by any endpoint. Admins can create and delete schedules via `POST` / `DELETE /api/v1/admin/schedules`; the create endpoint is generic over `JobType` but accepts only `NoOp` — `ImportCommodityPrices` is deliberately rejected with a pointer to its per-module surface (`PUT`/`DELETE /api/v1/finances/settings/price-sources/{source}/schedule`), and no other type is schedulable yet.
- Per-user, per-job real-time notifications over a SignalR hub. Schedule-spawned jobs inherit the schedule's creator and notify them like any other job; only a job with no creator (system-attributed — schema-supported, not yet produced by any endpoint) would emit no SignalR, its failures surfacing only in the admin job list.
- A read-only HTTP endpoint that lists jobs, scoped to the caller's identity (admins see all jobs; non-admins see only their own).
- Admin-only HTTP endpoints to list, create, and delete schedules.
- A best-effort orphaned-job recovery sweep at process startup.
- Programmatic pause for tests and migrations: `IJobProcessorControl.PauseAsync()` returns an `IAsyncDisposable` that drains all worker permits. **Pause does not stop the scheduler** — schedules can still fire during a pause; jobs accumulate as Pending and drain when pause is released. Tests that need the scheduler quiet set `JobSchedulingOptions.Enabled = false` via config.

## Out of scope for v1

- **Retries.** A failure is terminal. The user (or an admin) re-issues the work if they want it retried. A schedule that fires a job that fails will fire its next scheduled run at the normal cadence — there is no per-failure backoff.
- **External brokers.** No Redis, RabbitMQ, Hangfire, or background-process worker. The queue lives in the same database the rest of the app uses, and the worker is a `BackgroundService` in the WebApi.
- **Multi-process workers.** Concurrency is bounded by `JobProcessingOptions.MaxConcurrency` within a single process. Running multiple WebApi instances is not supported (the claim is atomic, but no other coordination exists).
- **Cancellation from the API surface.** A running job cannot be cancelled by the user. A 5-minute per-job timeout exists as a backstop, but there is no "cancel job" endpoint.
- **Per-job dead-lettering or quarantine.** Failed jobs stay in the table with their error string; there is no separate dead-letter table or replay tooling.
- **User-issued retries through the queue.** Reprocessing a single document version *is* available as a job type, but it is conceptually a fresh action initiated by the user, not a retry of a previous job.
- **Visibility into per-job progress beyond child counts.** Within a single non-parent job, the UI sees `Pending → InProgress → terminal` with no intermediate progress. Parent jobs expose `(completed + failed) / total` only.
- **Configurable timeouts or priorities at runtime.** Both are compile-time constants today (timeout) or set on enqueue (priority).

These are excluded from v1 but the model must not block adding them later. See *Future-compatibility*.

## Data model

Two tables back the subsystem: `processing.jobs` (the queue) and `processing.schedules` (the cron emitter).

### `processing.jobs`

One row per job. The columns the rest of the system depends on:

- **Identity:** `Id` (Guid PK).
- **What:** `Type` (enum, stored as string), `Payload` (jsonb — handler-defined shape), `EntityType` + `EntityId` (optional pointer to a domain entity the job concerns; used by the UI to link from a job back to its document/download). The job catalog below describes which handler reads which payload shape.
- **Status:** `Status` (enum), `Error` (free-form string from the failure exception's message), `Priority` (int, higher first).
- **Timing:** `CreatedAt`, `StartedAt`, `CompletedAt`.
- **Attribution:** `CreatedByUserId`. Audit logs written by the handler are attributed to this user, not the worker. Null for system-initiated jobs (those produced by a schedule with no creator).
- **Hierarchy:** `ParentJobId`, `TotalChildCount`, `CompletedChildCount`, `FailedChildCount`. See *Parent/child jobs*.
- **Origin:** `ScheduleId` (nullable FK → `processing.schedules`, ON DELETE SET NULL). Set when the job was created by the scheduler; null for ad-hoc enqueues. Lets the admin trace a job back to the schedule that produced it.

Indexes worth knowing:

- `(Status, Priority)` — supports the claim query.
- `EntityId`, `CreatedByUserId`, `ParentJobId`, `(ParentJobId, Status)` — support the list endpoint and parent-progress aggregation.
- `Type` — admin filtering.
- `ScheduleId` — admin "all jobs from schedule X" lookups.

### `processing.schedules`

One row per recurring trigger. Owned by the Processing module but populated by other modules (via migrations or per-module endpoints — Finances' price-source schedule endpoints are the first). The columns:

- **Identity:** `Id` (Guid PK), `Name` (display string), `Description` (optional admin note).
- **What to enqueue:** `JobType` (same enum as `processing.jobs.Type`), `Payload` (jsonb, opaque to the scheduler), `Priority` (int, copied to the spawned job).
- **When:** `CronExpression` (5-field, parsed by Cronos), `TimeZone` (IANA name, column reserved but only `Etc/UTC` honoured in v1).
- **Toggle:** `IsEnabled` (bool).
- **Attribution:** `CreatedByUserId` (nullable; null = system-attributed; the spawned job inherits this).
- **Tick state:** `LastRunAt`, `NextRunAt` (precomputed, the basis of the claim query), `LastJobId` (nullable FK → `processing.jobs`, denormalised for click-through; cleared on job delete).
- **Created:** `CreatedAt`.

Indexes: `(IsEnabled, NextRunAt)` for the claim query, `JobType` for admin filtering, `CreatedByUserId` for future per-user filtering.

The schedule table and the jobs table reference each other via `ON DELETE SET NULL` on both sides — they are loosely coupled through best-effort denormalisation, not invariants the queue depends on.

The `ParentJobId` foreign key cascades on delete so deleting a parent removes its children. Nothing else references `processing.jobs` by FK; jobs are not part of the audit graph.

## Status lifecycle

```
Pending ──┬─► InProgress ──► Completed
          │                  Failed
          │                  PartiallyCompleted   (parent jobs only)
          │
          └─► (recovered to Pending at startup if InProgress with no children)
```

- **Pending** — enqueued, not yet claimed.
- **InProgress** — atomically claimed by a worker (handler is running), or for parent jobs, the handler has returned and we are waiting on children.
- **Completed** — terminal success. For non-parent jobs, the handler returned without throwing. For parent jobs, every child completed successfully.
- **Failed** — terminal failure. For non-parent jobs, the handler threw. For parent jobs, every child failed.
- **PartiallyCompleted** — terminal. **Parent jobs only.** Some children completed, some failed.

A non-parent job will never enter `PartiallyCompleted`. A parent job that successfully spawned zero children would terminate as `Completed` immediately on the next progress update, since `0 finished ≥ 0 total` and `failed == 0`.

`CompletedAt` is set on every terminal transition (success or failure). `StartedAt` is set when the job is claimed, regardless of outcome. Recovered jobs (see *Crash recovery*) have their `StartedAt` cleared when transitioned back to `Pending`.

## The processor loop

The worker is a single `BackgroundService` (`JobProcessorService`) that also implements `IJobSignal` (a wake-up channel) and `IJobProcessorControl` (pause). Its loop:

1. **At startup**, sweep orphaned jobs (see *Crash recovery*).
2. **Wait** for either a wake-up signal or a 30-second poll timeout.
3. **Drain pending jobs**: while there is at least one available worker permit and at least one Pending job, atomically claim the highest-priority oldest one and dispatch the handler on a background `Task`.
4. **Loop.**

A few details that matter:

- **Atomic claim.** The claim is a `WHERE Status = Pending` `ExecuteUpdateAsync` that flips the row to `InProgress` and stamps `StartedAt`. If another path stole it, the update affects 0 rows and the loop tries the next candidate. This is what makes concurrent dispatch safe.
- **Ordering.** Highest `Priority` first (descending), with `CreatedAt` as a stable FIFO tiebreaker. The default priority is `0` — anything higher jumps the queue.
- **Bounded concurrency.** A `SemaphoreSlim` of size `JobProcessingOptions.MaxConcurrency` (default `1`) gates dispatch. The default of 1 keeps text-extraction and disk-bound work serialized; raising it is a deliberate, deployment-level choice.
- **Wake-up channel.** `JobQueue.EnqueueAsync` calls `IJobSignal.Signal()` after committing the row, which releases a permit on a `SemaphoreSlim(0)` the worker awaits. This makes the queue *latency-free for the common case* without polling. The 30-second poll exists as a safety net.
- **Per-job timeout.** Each handler runs against a linked `CancellationTokenSource` that cancels after 5 minutes. A timed-out job throws `TimeoutException` and lands in `Failed`. The application shutdown token also propagates — but a graceful shutdown awaits dispatched tasks before returning.
- **Handler resolution.** Handlers are auto-registered as scoped `IJobHandler` instances by assembly scan (`ProcessingServiceExtensions`). A job's `Type` is matched to the first handler with `handler.Type == job.Type`. Missing handler ⇒ the job lands in `Failed` with a "no handler registered" error and never blocks the queue.
- **Each handler runs in its own DI scope.** The processor creates a new `AsyncScope` per dispatched job, gets the scope's `AppDbContext` and `IEnumerable<IJobHandler>`, and the handler is free to `await db.SaveChangesAsync` directly. The processor does **not** wrap the handler in a transaction.

## Crash recovery

A job that was `InProgress` when the process died is **orphaned** — no worker is going to finish it. At startup, the processor runs:

```
UPDATE processing.jobs
SET Status = Pending, StartedAt = NULL
WHERE Status = InProgress AND TotalChildCount = 0
```

The `TotalChildCount = 0` filter is load-bearing: a parent job with children spawned is *legitimately* `InProgress` (it is waiting for children to terminate, which `UpdateParentProgressAsync` will do as each child finishes). Recovering such a parent would re-run its handler, double-spawn children, and corrupt the count.

This recovery is best-effort — a failure here is logged but does not prevent the worker from starting.

There is no recovery for failed jobs, no exponential-backoff retry, and no "stuck job" detection beyond the per-job 5-minute timeout. A handler that hangs without responding to its `CancellationToken` will not be killed before the timeout fires.

## Parent/child jobs

Some work fans out: bulk reprocessing N documents is N jobs, but the user issued one action and wants a single completion notification. The queue models this with a two-level hierarchy:

- A handler that intends to spawn children implements the marker interface `IParentJobHandler` (e.g. `BulkReprocessDocumentsJobHandler`).
- Inside `HandleAsync`, the parent inserts child jobs (with `ParentJobId = job.Id`), sets `job.TotalChildCount = N`, and saves. It does *not* set its own status to `Completed`.
- The processor recognises `IParentJobHandler` and leaves the parent in `InProgress` after the handler returns.
- As each child terminates, `UpdateParentProgressAsync` recomputes `(Completed, Failed)` from `processing.jobs WHERE ParentJobId = parent.Id`, updates the parent's counters, and — when `Completed + Failed >= TotalChildCount` — atomically transitions the parent to its terminal status:
  - all completed → `Completed`
  - all failed → `Failed`
  - mixed → `PartiallyCompleted`
- The terminal transition uses the same `WHERE Status = InProgress` guard as the claim, so only one concurrent caller wins. After it wins, the parent's user receives a single SignalR message with the terminal status and final counts.

Hierarchy is **flat**: children of a parent cannot themselves spawn children. There is no enforcement of this in the data layer — it is a convention. Any deeper nesting would break the progress-aggregation logic, which only walks one level up.

A parent handler must be **idempotent on restart**: if the process crashes after spawning children but before saving, the recovery sweep will not requeue it (because `TotalChildCount` was set as part of that save). If it crashes after writing the rows but before committing the count, the saved transaction is atomic so children either all exist or none do. The handler also defends against re-entry by checking whether children already exist for this parent ID (`existingChildCount > 0`) and short-circuiting.

## Notifications

A SignalR hub at `/hubs/processing` (`ProcessingHub`, `[Authorize]`) carries job status updates to the originating user. The hub is keyed by user ID — a job's notification only reaches the user named in `CreatedByUserId`. Jobs with no `CreatedByUserId` send no notification.

Each notification carries the `JobStatusChangedMessage`:

- `JobId`, `Status`, `EntityType`, `EntityId`, `DocumentName` (denormalised for client convenience).
- `Error`, `ErrorCode`, `ErrorData` — populated on failure. Structured error metadata is sourced from the thrown exception when it implements `IStructuredJobError`; otherwise only the free-form `Error` is set. This is how `DuplicateDocumentException` carries the duplicate's ID and name to the client without parsing the message string.
- `TotalChildCount`, `CompletedChildCount`, `FailedChildCount` — set on parent-job notifications only.

**When the worker emits a notification:**

- **Non-parent, success:** the handler itself emits the notification. (It is the handler's responsibility, not the processor's, because handlers vary in *when* "complete enough to tell the user" happens. The upload handlers, for instance, send the notification before generating thumbnails, treating thumbnail generation as a non-critical post-step.)
- **Non-parent, failure:** the processor emits the notification from its catch block, with the structured error if applicable.
- **Child of a parent, success:** the child handler **suppresses** its own notification (`if (job.ParentJobId is null)`). The user only sees the rolled-up parent notification.
- **Child of a parent, failure:** the processor's catch block emits the failure notification. The user therefore sees per-child error toasts plus a parent summary. This is deliberate — individual child errors carry actionable detail (which document failed, why) that is lost in the aggregate.
- **Parent, terminal or progress update:** `UpdateParentProgressAsync` emits one notification per child terminating, with the parent's running counts. The terminal transition emits the final one.

The frontend hub client (`ProcessingHubService`) distinguishes **managed** jobs (the initiator awaits `waitForJob(jobId)` because it is going to handle the result inline — e.g., a dialog waiting on the document upload) from **unmanaged** jobs (background work whose completion should surface as a generic toast). Managed jobs suppress the generic toast; unmanaged ones receive `toast.success`, `toast.warning`, or `toast.error` based on the terminal status.

## Authorization and visibility

- **Enqueue** is gated by whichever endpoint accepts the user's request. The queue itself does not check authorization — the calling feature's handler does. `CreatedByUserId` is filled from the current user for user-initiated enqueues; schedule-spawned jobs inherit the schedule's `CreatedByUserId`, which may be null (system-attributed).
- **Listing** is gated by `AuthPolicies.RequireContributor` (the standard authenticated-user policy). The `GetProcessingJobs` handler then narrows the result set:
  - Non-admin users see only jobs where `CreatedByUserId == currentUser.UserId`.
  - Admins see all jobs.
  - By default the list returns top-level jobs only (`ParentJobId IS NULL`); pass `ParentJobId=<guid>` to drill into a parent's children.
- **Receiving notifications** is gated at the hub layer by the same auth policy and at the message routing layer by `Clients.User(userId)`. Non-creators do not receive a job's events, even if they could see the job in the admin list.

## The pause control

`IJobProcessorControl.PauseAsync()` waits to acquire all `MaxConcurrency` semaphore permits, then returns an `IAsyncDisposable` that releases them on dispose. While a pause handle is held, no new jobs can be claimed.

This exists for **tests and for any future operation that needs the queue quiescent** (a migration, a maintenance task, etc.). It is the only mechanism for stopping the worker without shutting the host down. There is no UI or admin endpoint for it in v1 — it is purely an in-process API.

## Job catalog

Most v1 domain job types are initiated by the Documents module, with handlers under `Modules/Documents/Processing/`; Finances contributes `ImportCommodityPrices`, with its handler under `Modules/Finances/Processing/`. There is also a `NoOp` infrastructure job type used for smoke testing the queue and scheduler — its handler lives next to the queue framework, not under any module.

A note on the two hard constraints that shape any new fan-out: the **5-minute per-job timeout** is a compile-time constant, and `MaxConcurrency` defaults to **1**, so a long job does not merely risk timing out — it blocks every upload behind it. Work whose size scales with the library (the backup mirror is the canonical case) must therefore split into fixed-size batches at low priority, with the parent doing only the DB-bound planning. See `BackupToTarget` / `BackupBatch` below.

### `UploadDocument`

Triggered when a new document is uploaded. The HTTP endpoint stages the file under `media/<UploadsSubroot>/<UploadId><ext>` and enqueues this job. The handler:

1. Reads the staged file, runs text extraction (best-effort — failure logs a warning but the upload proceeds).
2. Computes the SHA-256 hash; rejects the upload as a duplicate if any other version (including in trashed documents) shares the hash. The duplicate exception is `IStructuredJobError` so the client can offer "view the existing document".
3. Moves the file from staging to its hash-addressed final location (copy + delete to be safe across filesystems).
4. Creates the `Document` and its first `DocumentVersion` in the same transaction; rolls back the file move on DB failure.
5. If auto-classify is enabled (the `AutoClassifyOnUpload` setting plus AI being configured), renders the first-page image and runs classification **inline** at full `All` scope, folding the result into document creation. Non-fatal — a classification failure logs a warning and the upload still succeeds.
6. Sends the SignalR completion notification — deliberately **after** classification, so the UI sees the AI-suggested name/tags/parties/summary the moment the upload lands.
7. Generates a thumbnail (post-notification, best-effort).

`Document.SearchVector` is never written by application code: a database trigger recomputes it automatically whenever the document's `Name`/`Summary` or the latest version's extracted text changes, so creating the row and its version in step 4 (plus any name the classification in step 5 applies) keeps it current — there is no explicit search-vector step. See [search.md](../documents/search.md) and migration `AddSearchVectorTriggers`.

Sets `EntityType = Document`, `EntityId = documentId`.

### `UploadDocumentVersion`

Triggered when a new version of an existing document is uploaded. Same shape as `UploadDocument` — minus the auto-classify step, since re-uploading a file does not re-classify — but creates only a `DocumentVersion` (with `VersionNumber = max(existing) + 1`) attached to the existing document. Sets `EntityId` to the *parent* document.

### `ReprocessDocumentVersion`

Re-runs text extraction on an existing document version (e.g., after the user changes the OCR engine, or to re-extract one document with a different engine). The handler reads the file from its hash-addressed location, re-extracts text, and writes the diff to the audit log via `auditLogger.ForUser(job.CreatedByUserId)`. Persisting the new extracted text fires the database trigger that refreshes the parent document's search vector (when the reprocessed version is the latest) — there is no app-side vector update.

The engine is **not** resolved here: the payload carries an already-resolved, non-nullable `EngineKey`, stamped at enqueue (see [Documents › versions](../documents/versions.md)). A bulk fan-out copies the parent's key into every child, so one batch can never split across engines if the household default changes mid-run. Reprocess also records extraction provenance onto the version alongside the text.

Failures here are loud: text extraction failure throws `TextExtractionFailedException` (no graceful "proceed without text" path, unlike upload). When run as a child of a `BulkReprocessDocuments` parent, this handler suppresses its own success notification — the parent's progress updates are the sole channel.

### `BulkReprocessDocuments` *(parent)*

Triggered by the bulk-reprocess action on the documents list. The handler:

1. Idempotency check: if children already exist for this parent ID (crash recovery), restore `TotalChildCount` and return.
2. Resolve the latest version per supplied document ID.
3. Insert one `ReprocessDocumentVersion` child per `(documentId, latestVersionId)` with `ParentJobId = job.Id`.
4. Set `TotalChildCount = N`, save, and signal the worker.
5. Return — the parent is left `InProgress`; child completions drive the rest.

### `ClassifyDocument`

Enqueued as a child of a `BulkClassifyDocuments` parent — one per document. Both interactive bulk classify **and** the detail page's single-document classify run through this path (single-doc classify enqueues a one-item bulk job, so its image-carrying classification stays on the background path). The one classification path that does **not** spawn this job is auto-classify-on-upload, which calls the classifier inline inside `UploadDocument` (see above). The handler:

1. Load the document with its current tags, parties, and any pending suggestions.
2. Gate on AI being configured (the resolved `IAiProvider` is available). If it is not, skip classification but still send a notification.
3. Load the latest version's `ExtractedText`, render the first-page image (scope permitting — see the [overview](../documents/overview.md#ai-classification-and-summarization)), and load the available tags and parties.
4. Call `IAiClassificationService.ClassifyAsync()` — builds a prompt with the document's name, extracted text, and the in-scope tag/party catalogs, attaches the page image when present, and parses the structured JSON response.
5. Clear any prior pending suggestions: revert AI-applied tags, name, and summary to their pre-AI state. Persist deletes before applying new data (two-save pattern to avoid composite key collisions on the `DocumentTag` join table).
6. Auto-apply the new classification: add AI-suggested tags (skipping tags already on the document), set the suggested title, and set the summary. A `DocumentSuggestion` record is created for each applied **tag, party, and name** — but **not** for the summary, which is written directly and is purely AI-managed (no approve/decline).
7. Send a SignalR notification. (Setting the document's name/summary in step 6 fires the database trigger that refreshes the search vector — there is no app-side vector update.)

When run as a child of `BulkClassifyDocuments`, the handler suppresses its own success notification — the parent's progress updates are the sole channel.

Sets `EntityType = Document`, `EntityId = documentId`.

### `BulkClassifyDocuments` *(parent)*

Triggered by the bulk-classify action on the documents list (and by the detail page's single-document classify, which enqueues a one-item bulk). The handler fans out to one `ClassifyDocument` child per document. Follows the same idempotent parent pattern as `BulkReprocessDocuments` — checks for existing children on re-entry and short-circuits. Per-document write authorisation is verified before enqueue.

### `DownloadDocuments`

Packages a set of documents into a ZIP for download. The handler reads metadata (document name, tag paths, party names, latest version's file path), builds an archive with collision-resolved entry paths via `IDownloadPathBuilder`, and writes the ZIP to a temp location keyed by `job.Id`. On success, the handler **rewrites the job's payload** to `DownloadResultPayload(zipFileSize, documentCount, expiresAt)` so the API can serve the result. Sets `EntityType = Download`. The ZIP expires after a fixed window; expired files are swept on the next download job.

This is the only v1 job type whose `Payload` carries result data after completion — most jobs use `EntityId` to point back to the produced entity.

### `ImportCommodityPrices`

The Finances module's commodity-price import (handler under `Modules/Finances/Processing/`). Enqueued ad-hoc by the admin *Run import now* action (`POST /api/v1/finances/settings/price-sources/{source}/runs`) and recurringly by per-source schedules managed via `PUT`/`DELETE /api/v1/finances/settings/price-sources/{source}/schedule`. The payload names the price source plus optional commodity-ID and date-window overrides. The handler:

1. Verifies the source is external and enabled (`CommodityPriceSourceSettings`).
2. Resolves the source's keyed `IPriceProvider` (Bank of Canada Valet for FX, Alpha Vantage for equities).
3. Builds the commodity set: held commodities (split commodities, unit-price commodities, reporting currencies) plus the source's pair allow-list, filtered to provider-supported pairs/exchanges.
4. Computes the import window — explicit payload override, else a watermark anchor (the earliest of each held pair's latest price, minus the provider's revision lookback), else earliest account activity.
5. Imports observations and applies them via `ApplyCommodityPriceObservationsCommand` through the standard unit-of-work pipeline. Scheduled runs stay system-attributed in the audit log; manual runs are attributed to the initiating user.

It emits no SignalR success notification of its own — import status surfaces on the price-source settings row (`LastImportStatus` / `LastImportMessage`). See [finances/commodities.md](../finances/commodities.md#price-sources--model).

### `BackupToTarget` *(parent)*

The Documents module's one-way backup mirror — the **planner**. Enqueued ad-hoc by the admin *Back up
now* / *Dry run* actions (`POST /api/v1/admin/backup/targets/{key}/runs`) and recurringly by per-target
schedules managed via `PUT`/`DELETE /api/v1/admin/backup/targets/{key}/schedule`. The payload names the
target key plus `dryRun` / `allowLargePrune` flags. The handler:

1. Idempotency check: if children already exist for this parent ID (crash recovery), restore
   `TotalChildCount` and return.
2. Stamp `EntityType = BackupTarget`, `EntityId = DeterministicGuid.FromKey(targetKey)` onto itself — a
   *scheduled* run cannot be stamped at enqueue (the scheduler copies only the schedule's payload), and
   without it the parent would be invisible to the single-flight query while its children drain.
3. Resolve the target. Unavailable (misconfigured) **fails loudly** — a mirror that reports success
   while copying nothing is the worst outcome for a backup. Disabled, or another run already in
   progress, **skips** with a reason staged into `Result` and zero children.
4. Project every version in the instance — *including* versions of trashed documents — and diff the
   stored `FileHash` against the target's state rows. Latch the delete circuit breaker if it trips.
5. Dry run: stage the change-set summary into `Result` and spawn **zero** children (the processor
   terminates a childless parent as `Completed`). Otherwise insert one `BackupBatch` child per batch.

**The handler does no file I/O**, deliberately: the diff is a pure comparison of stored hashes against
stored state rows, so it is bounded by two queries rather than by how many gigabytes need copying, and
it fits the 5-minute per-job timeout on a library of any size.

Sets `EntityType = BackupTarget`, `EntityId = DeterministicGuid.FromKey(targetKey)`.

### `BackupBatch`

Executes one slice of a backup plan — up to 100 files or 512 MB of copies, or up to 500 metadata-only
moves/deletes. Enqueued only as a child of `BackupToTarget`. Per action it streams the blob to verify
its SHA-256 **before** writing, copies it to the target, and persists the state row **only after** the
write returns. A missing source file is a skip (the version was permanently deleted mid-run); a hash
mismatch is a failure. Per-action failures are collected, successful state rows are saved, and the batch
throws once at the end — so one bad file yields `PartiallyCompleted` at the parent instead of costing
the other ninety-nine their state rows. Suppresses its own success notification as a child.

**Why the fan-out exists:** a single "back up everything" job would blow the 5-minute timeout on the
first run of any real library and starve the queue behind it (`MaxConcurrency` defaults to 1). Batches
are enqueued at **negative** priority, descending by batch index — negative so uploads and classification
preempt the mirror between batches, descending so the claim order preserves the planner's
delete-then-move-then-put ordering (deletes free the paths that later moves and puts reuse).

See [documents/backup.md](../documents/backup.md) for the product-level contract.

### `NoOp`

A trivial handler that returns immediately. Exists so the queue + scheduler pipelines can be smoke-tested end-to-end (cron tick → enqueue → worker dispatch → terminal status) without depending on any real domain handler — particularly useful for integration / E2E test scaffolding and for an admin verifying a deployment. Reads no payload, writes no DB rows, emits no SignalR. The processor still flips the row to `Completed` automatically, so admins see it land in the read-only job list. Lives at `Homestead.WebApi/Processing/NoOpJobHandler.cs` rather than under a module — it has no domain coupling.

## Design principles

### One process, one queue, one database

The queue is a table in the same PostgreSQL the rest of the app uses. The worker is a `BackgroundService` in the same WebApi process. There are no external dependencies. This is the right shape for a self-hosted single-household deployment: one process to run, one database to back up, no extra moving parts. It is **not** the right shape for horizontal scale-out — running two WebApi instances would have them race to claim jobs, which the atomic claim handles, but no other coordination (e.g., scheduled work, leader election) exists. The product is single-instance and that is intentional.

### Handlers own their domain transactions

The processor does not open a transaction around `HandleAsync`. Each handler manages its own `SaveChangesAsync`, its own audit logging, its own file-system compensation. This keeps the processor agnostic to handler complexity and makes it natural for a handler to mix DB writes with side-effects (file moves, search-vector updates) and decide when "complete enough to notify the user" is. The price is that a handler crashing mid-transaction must clean up after itself; the upload handlers do this by deleting the placed file on save failure.

### No retries — failures are user-visible

A failed job stays failed. The user sees the failure message in real time and decides what to do. This is the right default for this product because most jobs are user-initiated (an upload, a reprocess); silently retrying would either succeed surprisingly late or pile up duplicate effort. If retry semantics are needed later — for a job class where the user does *not* want to be told about transient failures — that becomes an opt-in attribute of the job type, not a queue-wide policy.

### Notifications are the handler's responsibility, except on failure

Success notifications come from the handler because only the handler knows when "user-visible done" has happened (e.g., after the DB commit but before optional thumbnail generation). Failure notifications come from the processor because by the time it has caught an exception, the handler is no longer in a position to publish a coherent message. Structured error data (`IStructuredJobError`) bridges the gap so the processor's catch block can still surface domain-specific detail.

### Parent jobs are flat fan-out, not a workflow engine

The hierarchy supports exactly one shape: a parent that creates N independent children at once and waits for them all. There is no chain-of-jobs, no DAG, no conditional next-step. If a future feature needs that, it should be built explicitly on top of the queue (a state machine in a domain handler), not by extending the hierarchy depth.

### The audit attribution rule

Background-job handlers that write to the audit log use `auditLogger.ForUser(job.CreatedByUserId)` — never the current request's user (there is none) and never anonymous. This keeps the audit log honest about *who caused* a change, even when the change ran asynchronously from a job queue. Any new handler that mutates auditable state must follow this pattern.

## Distinctive UX decisions worth preserving

1. **The job ID returned from enqueue is the contract.** Every async-initiating endpoint returns `{ jobId }`. Frontends decide whether to await the SignalR completion (managed jobs) or fire-and-forget with a generic toast on completion (unmanaged jobs).
2. **The admin job list is read-only.** No retry button, no cancel button, no force-fail. If a job needs intervention, the user re-issues the action.
3. **Bulk operations show *partial* failure as a first-class outcome.** `PartiallyCompleted` is not a degenerate `Failed`; the toast wording (`"Processing completed with N failed item(s)"`) tells the user some items succeeded and which did not.
4. **Job notifications are scoped per user.** Two users in the same household uploading concurrently do not see each other's toasts.
5. **The list view groups by parent.** Children only appear when their parent is expanded. The default view is "what work the user kicked off," not "every individual operation."

## The scheduler

A second `BackgroundService` (`JobSchedulerService`) ticks once per minute (configurable via `JobSchedulingOptions.PollIntervalSeconds`) and emits Pending jobs from the `processing.schedules` table. The loop:

1. Read the most overdue enabled schedule (`WHERE IsEnabled AND NextRunAt <= now()` ordered by `NextRunAt`).
2. Compute the schedule's next future occurrence via Cronos.
3. Atomically claim it: `WHERE Id = X AND IsEnabled AND NextRunAt = <captured>` `ExecuteUpdateAsync` to set `LastRunAt = now()` and `NextRunAt = <next-future-occurrence>`. If 0 rows updated, skip — another tick won.
4. Enqueue a `processing.jobs` row via the same `IJobQueue` ad-hoc enqueues use, with `ScheduleId = schedule.Id`. This calls `IJobSignal.Signal()`, so the worker dispatches immediately.
5. Update `LastJobId` on the schedule (best-effort denormalisation; not load-bearing).
6. Loop back; drain everything else currently due before sleeping.

**Catch-up policy:** *fire once per due schedule, then advance `NextRunAt` to the next future occurrence past `now()`*. If the host was down for two days, a daily schedule fires once on resume — handlers that need real backfill compute it themselves from `LastRunAt`. There is no per-tick replay.

**Crash recovery:** none. The (`LastRunAt`, `NextRunAt`) pair is the single source of truth; a crash mid-fire either persisted the advance (the job runs whenever the worker wakes) or didn't (the schedule fires on the next tick).

**Invalid cron:** if a schedule's `CronExpression` fails to parse, the scheduler logs an error and disables the schedule (`IsEnabled = false`). The admin sees a disabled schedule in the list and can investigate.

**Pause behaviour:** `IJobProcessorControl.PauseAsync()` only pauses the *worker*. The scheduler keeps ticking and may queue Pending jobs during a pause; they drain when pause is released. Tests that need the scheduler quiet set `JobSchedulingOptions.Enabled = false`.

Schedules can be created and deleted via admin endpoints: `POST /api/v1/admin/schedules` and `DELETE /api/v1/admin/schedules/{id}`. The create endpoint is shaped generically over `JobType` but accepts only `NoOp` — `ImportCommodityPrices` is deliberately rejected with a message pointing to its per-module surface, and no other recurring handler exists yet. There is no update endpoint (toggling `IsEnabled` requires delete + recreate). Per-module CRUD is no longer hypothetical: Finances manages its import schedules via `PUT`/`DELETE /api/v1/finances/settings/price-sources/{source}/schedule`, upserting rows in the same shared table (see *Future-compatibility*).

## Future-compatibility

The following must be addable without rewriting the tables or breaking existing job types:

- **Retries with backoff** as an opt-in property of a job type. The `Error` column already stores the latest failure; adding `Attempt` / `MaxAttempts` / `NextAttemptAt` columns is the natural extension.
- **Per-schedule retry/backoff for failed runs** — today a failed scheduled run is terminal; the next tick fires at the next normal occurrence. Adding a `RetryPolicy` column on `processing.schedules` (or job-type-level) is the natural place.
- **Scheduled one-off jobs** (`RunAt` on `processing.jobs`) — the claim query becomes `WHERE Status = Pending AND (RunAt IS NULL OR RunAt <= now())`. Distinct from recurring schedules: this would be "remind me in 10 minutes" semantics.
- **Per-module schedule CRUD endpoints** — shipped for Finances: `PUT`/`DELETE /api/v1/finances/settings/price-sources/{source}/schedule` filter the shared table by `JobType = ImportCommodityPrices` and render module-friendly forms, while the generic admin create deliberately rejects that type. New modules adding recurring work should follow the same pattern. The admin endpoint stays intentionally minimal — schedule update / `IsEnabled` toggle and a `Payload` field on create are deliberate omissions to be added when there is a consumer that needs them.
- **Per-schedule timezone honour** — `TimeZone` column already exists; v1 always treats it as `Etc/UTC`. Honouring it changes only the Cronos `GetNextOccurrence` call.
- **Per-job-type concurrency caps** (e.g., one OCR at a time, but ten ZIPs at a time). Today's single semaphore would split into per-type semaphores.
- **Cancel from the admin UI** — the worker would need to expose a per-job cancellation token, and the API would set a `CancelRequested` flag the worker checks.
- **External worker process** — the in-process `BackgroundService` is replaced by a separate executable that talks to the same table. The atomic-claim primitive already supports this.
- **Multi-process WebApi** — beyond the queue, this needs a coordinated answer for SignalR (a backplane) and startup recovery (only one node should sweep). Not a queue concern in isolation.
- **Deeper hierarchies / DAG workflows** — would not extend `ParentJobId`; build a domain-level state machine that issues fresh top-level jobs and reads their results.

None of these should require re-migrating historical rows. The current schemas' nullable `EntityType`, `EntityId`, `Payload`, `CreatedByUserId`, `ParentJobId`, and `ScheduleId` are deliberately permissive so future job and schedule types can fit without an ALTER TABLE.
