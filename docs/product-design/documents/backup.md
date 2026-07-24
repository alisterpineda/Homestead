# Documents — Backup mirror

This document describes Homestead's **one-way document backup mirror**: what it guarantees, what it
deliberately refuses to do, and the decisions that shape it. It is a Documents-module feature with an
Admin-module control surface (`/admin/backup`) and it runs entirely on the
[Processing](../processing/overview.md) queue.

The *framework* it plugs into is deliberately not Documents-specific: a **backup source** pairs a
code-registered source kind with a config-defined target, and the admin surface, the shared run payload,
and the shared run summary are generic across kinds. Documents is the only kind today. What is
Documents-specific — the planner, the layout, the change detection, the state table — is what the rest
of this page is about; see [The source/target model](#the-sourcetarget-model) for the generic half.

## Purpose

Without this feature there is **no way to get documents out of the deployment**. Blobs live only under
`FileStorage:MediaPath` in a hash-addressed tree (`documents/files/{hash[..4]}/{hash}{ext}`) whose
filenames are meaningless without the PostgreSQL metadata, and the only export is a manual, pull-based
ZIP. If the disk dies or the app is abandoned, the library is effectively unreadable.

The mirror reproduces every document version onto a **separate filesystem root** as human-named files in
a browsable tree, so the backup is usable **with no Homestead running at all**. That is the single
requirement everything else follows from: the mirror is readable by a person with a file browser.

## In scope for v1

- **Instance-wide scope.** Every document in the instance, with no owner, group, or tag filter. A backup
  that silently excluded some documents would be worse than no backup, because you would not find out
  until you needed it.
- **Files only.** No manifest, no sidecar metadata, no exported tags/parties/notes. The tree itself is
  the index.
- **All versions.** The latest version of a document sits at the shard root; every superseded version
  goes in that shard's `_versions/` folder with a ` (vN)` suffix.
- **Mirror deletes**, guarded by a bulk-delete circuit breaker (see below). The mirror converges on the
  library; it does not accumulate forever.
- **Targets are config instances** under `Backup:Targets:{key}` — the same shape as `Ocr:Engines` and
  `Ai:Providers` — not in-app CRUD. In Phase 1 the only kind is `Local` (a directory the WebApi process
  can write to: local disk, mounted NAS share, bind-mounted volume).
- **What gets mirrored where is an admin decision**, made in-app: an admin adds a *backup* — one source
  kind paired with one target — at `/admin/backup`. Nothing is backed up until they do.
- **Scheduled or manual runs**, managed per backup at `/admin/backup`: a cron schedule, a *Back up now*
  action, and a *Dry run* that reports the change set without touching anything. A dry run is a **plan
  preview, not a preflight check** — the planner does no file I/O, so it cannot foresee a failure that
  only the copy itself would hit (a missing or corrupt source blob).

## Out of scope for v1

- **Two-way sync.** Nothing is ever read back from the mirror into Homestead. There is no import, no
  conflict resolution, and no notion of the destination being authoritative about anything. A file
  edited or deleted on the target is overwritten or re-created on the next run.
- **Restore tooling.** Recovery is "read the files with a file browser" — which is the whole point of a
  human-readable layout. There is no in-app restore flow.
- **Cloud targets.** Deliberately deferred to Phase 2. The destination is abstracted behind
  `IBackupTarget` so a Google Drive target can be added without touching the planner, the jobs, or the
  schema, but Phase 1 ships only the local/NAS target and carries zero cloud dependency.
- **Per-document or per-tag backup selection.** See *instance-wide scope* above.
- **Encryption or compression at the target.** The mirror is plain files; encryption is the storage
  layer's job (an encrypted volume, an encrypted NAS share).
- **Verifying anything.** A run trusts its own state table rather than inspecting either end — see
  *Change detection*. Neither a destination corrupted out-of-band nor a **source** blob that vanishes
  after it was mirrored is detected: the recorded hash still matches the version's, so no action is
  planned and nothing ever stats the file. That is the right trade for a mirror (the backup copy is
  retained rather than needlessly re-verified or re-copied), but it means the backup page can only ever
  surface a missing blob for a version it has **not yet** copied. It is not a library integrity check.

## The source/target model

Two halves, split along one line: **config states environment-bound facts, the database holds
operational choices.**

- **Targets** are static config (`Backup:Targets:{key}`) — a filesystem path today, cloud credentials
  later. They are facts about the deployment, so they are not editable in-app. The only DB state a
  target carries is an operator enable toggle, created lazily.
- **Source kinds** are a code registry. Each module that supports being backed up registers a
  descriptor: its key, display name, planner job type, the folder it owns at the target root, and
  whether it can prune. A DB row can only name a kind that code offers — the same relationship a
  `ProcessingSchedule` has with `ProcessingJobType`.
- **A backup source** (`backup.sources`) pairs one kind with one target. It owns everything
  operational: whether the pairing is enabled, its schedule, and its delete-breaker latch. Its id is the
  `EntityId` of every run and batch job it spawns.

**One pairing per kind per target, enforced by a unique index on `(source_kind, lower(target_key))`.**
This is a correctness invariant, not tidiness: two same-kind pairings on one target would share a root
folder, each keep its own state rows, and therefore each see the other's files as unknown and prune them
by anti-join — the two would punch holes in each other's mirrors indefinitely. Target keys compare
case-insensitively because config keys do, which is why the index is on an expression. (Allowing scoped
backups later — two rows, same kind, same target — requires a per-row folder/scope column first.)

A pairing whose target vanished from config, or whose kind is no longer registered, is **never hidden**.
It renders unavailable-with-reason, because a backup that silently stopped is exactly what an admin
needs to see. Enforcement happens at the entry points: *Back up now* and schedule-upsert refuse with the
reason, and the planner is the last gate for drift that appears after scheduling.

### Deleting a backup

Deleting a pairing is an explicit "forget this backup": its state rows cascade away, its schedule row is
deleted with it, and the mirror's files are **left untouched** on the destination (the target is one-way
and nothing enumerates it, so orphans there are accepted; re-creating the same pairing does one full
re-put over them).

While a run is live, a plain delete **409s**. The flow then offers *cancel run and remove*, which
cancels the parent and its still-`Pending` children and deletes the row **in one transaction** — so a
409 thrown partway rolls the cancellation back too, and a retry starts clean rather than finding a
half-cancelled run. A batch already inside its handler cannot be cancelled by anything, so that case
409s again with "a batch is still finishing — retry shortly" rather than tearing the row out from under
a job that is mid-write. The batch handler re-checks the source row at start as a backstop for the
narrow race either way.

## The layout

Each source kind owns a top-level folder at the target root, so several kinds can share one destination
without their prunes reaching into each other's files:

> **One-time note for pre-release mirrors.** The root folder was introduced after an earlier build wrote
> straight to `<root>/2024/…`. Because the migration wipes the state table and deletions are found only
> by anti-join against it, a re-created pairing re-puts the whole library at the new paths and cannot
> see the old un-prefixed folders — they linger as a second full copy forever. Delete them by hand
> after the first run of the new layout.

```
<root>/
  Documents/                               ← the documents kind's root folder
    2024/
      Tax Return 2024.pdf                  ← latest version
      _versions/
        Tax Return 2024 (v1).pdf           ← superseded
    2025/
      Car Insurance.pdf
    _undated/                              ← Document.DocumentDate is null
      Misc Receipt.pdf
```

- **Year shard.** The folder is `Document.DocumentDate?.Year`, falling back to `_undated` when the date
  is null. Sharding keeps any one folder small enough to browse, and by year is the axis a person
  actually reaches for when digging through household paperwork.
- **The shard is document-scoped, not version-scoped.** Every version of a document shares one folder
  and they all move together if the date is edited. A version tree split across two year folders would
  be unreadable, which defeats the point.
- **Names are sanitized**, not escaped: illegal and control characters become underscores, whitespace
  runs collapse, and leading/trailing dots and spaces are trimmed (Windows silently strips those, so
  `a. ` and `a` would otherwise be the same file). This is the same sanitizer the ZIP download uses.
  Party names are **not** appended — unlike the ZIP, this is a files-only mirror.
- **Collisions** — two documents resolving to the same name in one shard — are resolved by appending
  ` (<first 8 chars of DocumentId>)` to **every** member of the colliding group, including the one that
  "was there first". The rule must be a deterministic function of the current document set: an
  order-dependent counter (which the ZIP builder uses, where it only has to be unique within one
  archive) would silently rename files between runs and churn the mirror for no content reason.
- Names differing only in case collide, because the mirror may be read from a case-insensitive
  filesystem where they are one file.
- **The root folder is charged against the name-trimming budget**, not glued on afterwards — a name
  trimmed to the target's full path limit and *then* prefixed would overflow it.

## Change detection

Per `(backup source, version)` the planner compares desired state against a stored state row and emits
at most one action:

| Condition | Action |
|---|---|
| No state row | `Put` |
| Stored hash ≠ current `FileHash` | `Put` (replace) |
| Path differs, hash same | `Move` |
| State row whose version no longer exists | `Delete` |
| Otherwise | *(no action)* |

Because version blobs are immutable and content-addressed (`DocumentVersion.FileHash` carries a UNIQUE
index), **an unchanged version is never re-read or re-uploaded after its first successful copy**. The
steady-state run costs two queries and zero file I/O.

Deletions are found by **anti-join against the state table**, never by cascade. The row's
`DocumentVersionId` is FK-less and outlives the version it describes, because at the moment a version is
permanently deleted that row is the only record of what needs removing from the mirror — a cascade would
erase the instruction along with the reason for it. This follows the cross-cutting rule that
provenance-style references which must outlive their target stay FK-less, as audit logs do.

The deliberate asymmetry: the row's `BackupSourceId` *does* cascade, because deleting the pairing is an
explicit instruction to forget the whole backup rather than an incidental loss of one version.

### Trashed documents stay mirrored

Trash is a *restorable* state. A backup that evaporated the moment a file was moved to trash would be
exactly the copy you needed. Only a **permanent** delete removes the version row, and that is what the
anti-join picks up.

## The circuit breaker

The mirror deletes, which means a bug — or a restore of the PostgreSQL volume from an older snapshot
against an intact mirror — could wipe the only offline copy of the library. The breaker turns "delete a
lot" into an admin decision. It trips when:

```
deletes > max(MinAbsoluteDeletes /* 25 */, DeleteFraction /* 0.10 */ × already-mirrored)
```

Two thresholds, because either alone is wrong: a pure fraction trips constantly on a tiny library (3 of
20 files is 15%), and a pure absolute never trips on a large one. Strictly-greater, so a run sitting
exactly on the threshold proceeds — the guard is against a runaway, not against ordinary housekeeping.

**On trip, the run skips every delete but still performs every put and move.** Blocking prunes protects
data; blocking puts would lose it. The **source row** latches `PruneBlockedAt` / `PruneBlockedCount` —
not the target, because a latch is a fact about one dataset's change set, and clearing it from the
destination would dismiss every kind's warning there at once. `/admin/backup` shows a destructive banner
naming the count on that backup's row.

The admin clears it with an **explicit acknowledgement** (`PATCH /sources/{id}` with
`acknowledgePruneBlock`) or overrides it by re-running with *Prune anyway*. Acknowledgement is its own
flag rather than a side effect of any `PATCH`: pausing and resuming the backup must not silently dismiss
the banner, since the banner is the only place a blocked prune is reported. A later run with fewer
deletions does not silently un-latch it either — the warning has to be seen to be dismissed. (Deleting a
latched backup discards its un-acknowledged warning; accepted.)

Scheduled runs never carry the override: an unattended run that could prune the mirror wholesale is
precisely what the breaker exists to stop.

## The admin surface

`/admin/backup` lists one card per target — config targets plus any target key that survives only on a
source row — and nests that target's backups inside it.

The **card** carries what belongs to the destination:

- Config (key, kind, path) and an availability badge. A misconfigured target reads as
  **unavailable, because &lt;reason&gt;** rather than preventing the app from booting — the same
  error-string posture the OCR engines and AI providers use.
- A destination enable toggle, which pauses every backup writing here (a target with no state row reads
  as enabled: defining it in config *is* the opt-in).
- An *Add backup* picker, fed by the source-kind catalog the list endpoint returns alongside the cards,
  filtered to kinds not already paired here.

Each **backup row** under it carries what belongs to the pairing:

- Its display name, and its own availability with the reason when the kind or target has gone stale.
- Files mirrored and total size. An unknown byte total renders as `—`, never `0 B` — a planner that
  generates its content cannot know sizes at plan time, and "unknown" must not read as "nothing".
- The last run's status and change-set summary, plus its failure messages when it failed.
- The cron schedule, with add / edit / remove. Editing updates the schedule **in place**, keeping its id
  stable — the id is a foreign key on the source row and is referenced by every job the schedule spawned.
- Its own enable toggle, *Back up now*, *Dry run*, — when latched — *Prune anyway*, and remove.

**Last-run status is derived from `processing.jobs`, not denormalized onto the backup schema.** A parent
job's terminal transition happens inside the generic processor, which offers no per-type hook, so a
handler-written status column could never be set reliably. Deriving it sidesteps the race and keeps the
state table to genuine state.

## Design principles

### The mirror is readable without Homestead

Every layout decision — year shards, human names, versions in a visible sub-folder, no manifest —
follows from one question: can a person with a file browser and no running app find their tax return?
A sidecar-metadata format or a hash-addressed tree would be more faithful and less useful.

### One-way is a guarantee, not a limitation

There is no read path from the target at all: `IBackupTarget` has no list or download member. Nothing in
the design can come to depend on what is out there over what the database says, which is what keeps
"the backup got edited" from becoming a class of bug.

### The planner does no file I/O

Job topology is exactly two levels: a `BackupToTarget` planner that diffs stored hashes against state
rows (pure database), fanning out to `BackupBatch` children that do all the streaming, hashing, and
writing. This is what keeps the planner inside the processor's hard-coded 5-minute per-job timeout on a
library of any size. Children run at negative priority so uploads and classification preempt the mirror
between batches. See [processing/overview.md](../processing/overview.md) for the job catalog.

### Paths are resolved once, by the planner

Children never re-derive a path; the parent stamps the resolved path into every child payload. This is
the same rule the OCR fan-out follows, and it is load-bearing here: collision suffixes depend on global
knowledge of the document set that a single batch cannot see.

### Content is verified before it is written, never after

A `Put` streams the blob to compute its SHA-256 and compares against the recorded hash **before** the
copy begins. The mirror is the copy of last resort, so propagating bytes that no longer match would
quietly replace a good backup with a corrupt one. The state row is written **only after** the target
write returns — a crash in between re-copies next run, which is harmless because the path is stable.

### A missing file is a race; a wrong file is a fault

Three distinct situations that all look like "the file isn't there":

- **The version row is gone** — permanently deleted between the plan and the batch. An expected race:
  the copy is skipped, and because the version no longer exists it is never re-planned.
- **The version row is still there but its blob is not** — the library has lost a file. This *fails* the
  action. Skipping it silently would write no state row, so the same copy would be re-planned on every
  future run: the mirror could never reach a steady state, while every run still reported a copy count
  that never happened.
- **The hash doesn't match** — real corruption. Fails the action rather than propagating bad bytes over
  a good backup.

Per-action failures are collected, everything that succeeded is persisted, and the batch throws once at
the end — so one bad file never costs the other ninety-nine their state rows.

### A failing run explains itself on the card

A parent whose *children* failed carries **no error string of its own**: the generic parent-progress
logic derives the parent's status from the child tally without writing one, and only a throw from the
planner itself populates `Error`. `GET /targets` therefore also returns each failed batch's message
(`batchErrors`, capped), which is the only place a per-file failure is actually named. `/admin/jobs`
remains the exhaustive record.

Two consequences worth knowing:

- **`PartiallyCompleted` needs more than one batch.** The processor derives a parent's status from its
  children, and a batch throws if *any* of its actions failed. A library small enough to fit in a single
  batch therefore lands on `Failed` even when most files copied fine. The badge is coarse by
  construction; the batch errors and the *Files mirrored* count are what tell the real story.
- **The change-set summary is the plan, not the outcome.** It is staged before a single byte moves, so
  the admin page labels it *Applied* only for a run that completed cleanly and *Planned* otherwise —
  a red badge beside a green-sounding copy count is exactly how a broken backup reads as a working one.

## Configuration

```
Backup:Targets:{key}:Kind    # Local (the only kind in Phase 1)
Backup:Targets:{key}:Path    # the mirror root
```

As env vars: `Backup__Targets__local__Kind`, `Backup__Targets__local__Path`. Zero configured targets
means backup is off — the graceful no-op, with no built-in destination seeded.

A `Path` that **overlaps** `FileStorage:MediaPath` or `FileStorage:CachePath` in either direction is
refused. Nested inside media storage, every run copies the library into itself; wrapped around it, the
mirror's own delete pass is pointed at the originals.

Batching and breaker knobs are **per source kind**, under that kind's own section — for documents,
`Backup:Documents:` — because the right numbers depend on what is being mirrored, not on the
destination: `MinAbsoluteDeletes`, `DeleteFraction`, `MaxFilesPerBatch`, `MaxBytesPerBatch`,
`MaxOpsPerMetadataBatch`. (A generated-export kind that replaces files wholesale would count far more
deletes for the same amount of real change, and needs its own thresholds.)

What is tunable where:

| Level | Database (admin) | Static config |
|---|---|---|
| Target | enabled | kind, path (later: credentials) |
| Backup (source row) | existence of the pairing, enabled, breaker latch/ack, schedule | batch caps, breaker thresholds (`Backup:{Kind}:*`) |
| Run | nothing stored — dry-run / prune-anyway are flags on that run | — |

## Future-compatibility

The following must be addable without re-migrating the tables or changing the planner:

- **A cloud target (Phase 2).** `IBackupTarget` + `BackupTargetCapabilities` already abstract the
  destination; `DocumentBackupState.RemoteHandle` already stores an opaque per-target identifier (a
  relative path locally, a file id on Drive), and `SupportsContentHash` is declared for a target that
  can verify a copy without downloading it back.
- **Per-target scoping** (by tag, group, or owner) — the planner takes a version set; narrowing that
  query is the whole change. Note this also needs a per-row folder/scope column before two same-kind
  pairings may share one target; see the uniqueness invariant above.
- **Sidecar metadata export** — a second action kind alongside `Put`/`Move`/`Delete`.
- **Verify runs** that reconcile against a listing of the destination — needs a read member on
  `IBackupTarget`, which is exactly why one-way is stated as a v1 guarantee rather than assumed forever.
- **Retention of superseded versions** (e.g. keep only the last N) — a filter in the path planner.
- **A second source kind** (e.g. a Finances export). It writes: a catalog entry, its own planner + batch
  handlers, its own state table keyed by `backup_source_id`, and optionally its own `Backup:{Kind}:*`
  knobs. The planner/plan-builder/batch engine stays in `Modules/Documents` until then, deliberately —
  documents' change detection leans on immutable content-addressed blobs with a stored hash, whereas a
  generated export needs watermark-style detection (fingerprint diff, `Put`+`Delete` instead of `Move`),
  so extracting a shared engine before a second real consumer exists would extract the wrong one.
- **A liveness marker file** (`.homestead-mirror`) probed before destructive operations, to catch an
  unmounted NAS mount point that still passes config-shape availability. Recorded, not built.
