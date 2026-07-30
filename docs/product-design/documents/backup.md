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
  can write to: local disk, mounted NAS share, bind-mounted volume); Phase 2 adds `GoogleDrive` (see
  [Phase 2 — the Google Drive target](#phase-2--the-google-drive-target)).
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
- **Cloud targets** were out of scope for v1, which ships only the local/NAS target and carries zero
  cloud dependency. The destination was abstracted behind `IBackupTarget` from the start so one could
  be added without touching the planner or the jobs; the Google Drive target is now designed — see
  [Phase 2 — the Google Drive target](#phase-2--the-google-drive-target).
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
My Drive/Homestead/                        ← app-owned namespace, Drive only (RootFolderPath)
  Backup/                                  ← the target root: map key "", the ONLY mapped level of this chain
    Documents/                             ← the documents kind's root folder
      2024/
        Tax Return 2024.pdf                ← latest version
        _versions/
          Tax Return 2024 (v1).pdf         ← superseded
      2025/
        Car Insurance.pdf
      _undated/                            ← Document.DocumentDate is null
        Misc Receipt.pdf
```

For a **local** target the chain above `Documents/` is just the configured `Path`; only the Drive target
has an app-created root path — see [The target root](#the-target-root). Paths in `backup.target_folders`
and `DocumentBackupState` are relative to the target root either way —
`Documents/2024/Tax Return 2024.pdf` — so nothing below the root knows or cares which kind of destination
it is on.

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

- Config (key, kind) and an availability badge. A misconfigured target reads as
  **unavailable, because &lt;reason&gt;** rather than preventing the app from booting — the same
  error-string posture the OCR engines and AI providers use.
- **Where the mirror is** — the configured directory for a local target; for Drive, the root folder as
  *actually created*, rendered as a link to it. Never the configured value once something exists there:
  a card that names a location the mirror does not live at is worse than no card. When config and the
  created root disagree, a one-line notice says so. See [The target root](#the-target-root).
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

Phase 2 extends the surface: every backup row gains a **"last successful run: N days ago"** staleness
line (also derived from `processing.jobs`), and a Drive target's card carries its connection state —
*Connect / Disconnect*, "Connected as …", Drive free space, and an async-validated availability badge —
plus the location link, which is the only way to answer "which of my Drive folders is the app's".
See [Phase 2 — the Google Drive target](#phase-2--the-google-drive-target).

**Last-successful-run excludes runs that mirrored nothing.** A *skipped* run — the pairing disabled, the
destination switched off — stages its skip reason, zeroes its child count and completes **normally**, and
a *dry run* does the same after copying nothing. Counted, either would let a paused backup report a fresh
success on every cron tick, forever. Since this line is the catch-all for every way a backup dies
silently, it is the last value in the system that may be easy to fake. A run with no staged summary at
all still counts: an older row that completed before summaries existed is a genuine run, and treating it
as a skip would invent staleness that never happened.

### The per-document read surface

`/admin/backup` answers "is this backup working?" at the pairing level. It cannot answer *"where did
**this** document go in the mirror, and is that backup even alive?"* — a question otherwise reachable
only by reasoning about year shards, collision suffixes and `_versions/` by hand. A **Backup** section on
the Metadata tab of the document detail page answers it.

- **Admin-only and read-only.** There is no per-document *Back up now*: per-document selection is out of
  scope (see above), and a backup that copies a hand-picked subset is not a backup. The only action is a
  link to `/admin/backup`.
- **Availability leads; the copy record is subordinate to it.** Disconnecting a target deliberately keeps
  its state rows, so a panel that led with the copy record would render a cheerful "backed up" against a
  destination nobody can reach. The order is: is this pairing reachable → when did it last actually
  mirror something → what does it record about this version → the document-level roll-up.
- **"Last written", never "copied".** A rename is planned as a *move*, which on an id-addressed
  destination is a metadata patch with no re-upload — and it still bumps the state row's timestamp. "Copied
  2 minutes ago" after a rename would be a copy event that never happened.
- **Absence of a recorded failure is not success.** A batch stages its per-file failures as typed data
  (version id, kind, path, message) so the panel can say *"the copy of this file failed: &lt;why&gt;"* —
  the thrown message is joined and truncated across the whole batch and cannot be attributed to one file,
  and a first-ever copy that fails writes no state row to join on. Where no failure was recorded — an
  older row, or a run that died without naming files — the panel says *"the last run failed; this file's
  outcome was not recorded"*, never something that reads as fine.
- **Facts, not a status enum.** The endpoint returns counts, timestamps, paths and reasons; the client
  does the phrasing. Verify runs then add fields rather than breaking a vocabulary. And there is no
  "verified" and no hash comparison: `DocumentVersion.FileHash` is written once at insert and never
  rewritten, so comparing it against the recorded backup hash could only ever agree with itself.
- **No "will move on the next run".** Collision suffixes are a function of the *whole* document set, and
  adding a version relocates the previously-latest file into `_versions/` under the same shard and stem —
  so any per-document approximation false-negatives on the most common case.
- **Copies are counted against live versions only.** State rows are FK-less tombstones that outlive the
  version they describe, so counting rows would report "3 of 2". The leftovers are reported separately as
  files still sitting in the mirror until a run's anti-join removes them — or forever, if the delete
  breaker has latched.
- **A link only while the pairing is usable.** The object URL is composed from the handle already on the
  state row (no listing, no read of the destination, so the one-way guarantee is untouched) and is
  suppressed entirely when the pairing is unavailable: a dead link after a disconnect or an account
  switch is worse than no link. A filesystem target has none — its handle *is* the relative path, and a
  `file://` URL would leak the server root and open nothing.
- **Known gap: a trashed document cannot reach it.** The page 404s under the global soft-delete filter,
  exactly as `GET /documents/{id}` does — while trashed documents *stay mirrored* (see
  [Trashed documents stay mirrored](#trashed-documents-stay-mirrored)). So the one class of document
  whose mirror state is most surprising is the one this surface cannot report on.

The panel carries its own refresh control rather than relying on real-time updates. Job notifications are
sent to the user who created the job, and a scheduled run carries the *schedule creator's* id — so the
nightly run, the common case, never reaches whichever admin happens to be reading the page.

## Design principles

### The mirror is readable without Homestead

Every layout decision — year shards, human names, versions in a visible sub-folder, no manifest —
follows from one question: can a person with a file browser and no running app find their tax return?
A sidecar-metadata format or a hash-addressed tree would be more faithful and less useful.

### One-way is a guarantee, not a limitation

`IBackupTarget` has no list or download member, and the planner plans from the database alone. Nothing
in the design can come to depend on what is out there over what the database says, which is what keeps
"the backup got edited" from becoming a class of bug.

The guarantee lives at the **interface and the planner**, not inside a target implementation. A target
may consult the destination internally where idempotency or housekeeping demands it — the local target
already enumerates a directory to decide whether it may be pruned, and the Drive target looks a name up
during crash recovery. What no target may ever do is let destination state overrule what the database
says should exist: destination lookups may answer "where is the thing I was told to write" — never
"what should exist".

### The planner does no file I/O

Job topology is exactly two levels: a `BackupToTarget` planner that diffs stored hashes against state
rows (pure database), fanning out to `BackupBatch` children that do all the streaming, hashing, and
writing. This is what keeps the planner inside the processor's hard-coded 5-minute per-job timeout on a
library of any size. Children run at negative priority so uploads and classification preempt the mirror
between batches, and that priority also **encodes the batch's pipeline stage** — `Delete`, then `Move`,
then `Put`, then `Prune`, each one step lower. Among one parent's children the queue treats a strictly
higher priority as a happens-before edge (it will not claim a job while a higher-priority sibling is
outstanding, and a failure cancels the siblings below it), so the plan's stage ordering is enforced
rather than merely hoped for. Batches within a stage share a priority and stay concurrent. See
[processing/overview.md](../processing/overview.md) for the job catalog.

### Paths are resolved once, by the planner

Children never re-derive a path; the parent stamps the resolved path into every child payload. This is
the same rule the OCR fan-out follows, and it is load-bearing here: collision suffixes depend on global
knowledge of the document set that a single batch cannot see.

### Content is verified before it is written, never after

A `Put` streams the blob to compute its SHA-256 and compares against the recorded hash **before** the
copy begins. The mirror is the copy of last resort, so propagating bytes that no longer match would
quietly replace a good backup with a corrupt one. The state row is written **only after** the target
write returns — a crash in between re-copies next run, which is harmless because the write is
idempotent (overwrite-by-path locally; find-and-overwrite in the Drive target's crash recovery).

"Never after" forbids *reading the copy back* to check it, not trusting a checksum the destination
reports in its write response — a target with `SupportsContentHash` (Drive) compares that reported
hash as a free second opinion, with no download.

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
Backup:Targets:{key}:Kind              # Local | GoogleDrive
Backup:Targets:{key}:Path              # Local: the mirror root
Backup:Targets:{key}:ClientId          # GoogleDrive: the admin's own OAuth client
Backup:Targets:{key}:ClientSecret      # GoogleDrive
Backup:Targets:{key}:RootFolderPath    # GoogleDrive, optional — default "Homestead/Backup"
```

As env vars: `Backup__Targets__local__Kind`, `Backup__Targets__local__Path`. Zero configured targets
means backup is off — the graceful no-op, with no built-in destination seeded.

A `Path` that **overlaps** `FileStorage:MediaPath` or `FileStorage:CachePath` in either direction is
refused. Nested inside media storage, every run copies the library into itself; wrapped around it, the
mirror's own delete pass is pointed at the originals.

`RootFolderPath` is a forward-slash path relative to My Drive, and it applies on **first creation only**:
once the root exists, the path as actually created is recorded on `backup.target_state` and every run
reads it from there. Editing config afterwards does not relocate anything — see
[The target root](#the-target-root) for why, and for the notice the admin card shows
when the two disagree. It is validated for shape (no `\`, no leading/trailing `/`, no empty, `.` or `..`
segments, ≤255 bytes per segment, ≤1024 characters overall) and, like every other config problem here,
reports as *unavailable, because …* rather than stopping the app from booting.

Batching and breaker knobs are **per source kind**, under that kind's own section — for documents,
`Backup:Documents:` — `MinAbsoluteDeletes`, `DeleteFraction`, `MaxFilesPerBatch`, `MaxBytesPerBatch`,
`MaxOpsPerMetadataBatch`. The *breaker* thresholds genuinely depend on what is being mirrored (a
generated-export kind that replaces files wholesale counts far more deletes for the same amount of
real change), but Phase 1's claim that the batch caps do too was too absolute: for a cloud target the
binding constraint is destination latency and uplink throughput, not the shape of the dataset. The
batch caps therefore also accept **per-target overrides** (`Backup:Targets:{key}:MaxFilesPerBatch`
etc.), which win over the kind defaults; the Drive kind ships with much smaller effective defaults —
see [Deadline economics](#deadline-economics-budgeted-batches-continuations-resumable-sessions).

What is tunable where:

| Level | Database (admin) | Static config |
|---|---|---|
| Target | enabled; the OAuth grant (encrypted refresh token, connected account, issued-at), auth-failed latch, folder map, the *resolved* root path + its Drive link — cloud kinds only | kind, path / client id + secret / root folder path (first creation only); per-target batch-cap overrides |
| Backup (source row) | existence of the pairing, enabled, breaker latch/ack, schedule | batch caps, breaker thresholds (`Backup:{Kind}:*`) |
| Run | nothing stored — dry-run / prune-anyway are flags on that run | — |

The target row's split follows the same line as everything else: the OAuth *client* is an
environment-bound fact and lives in config; the *grant* is obtained interactively at runtime — the one
thing config cannot express — and lives in the database, exactly as the enable toggle already did. The
root folder is the same shape once more: config names where to *put* it, the database records where it
actually *is*.

## Phase 2 — the Google Drive target

`GoogleDrive` is the second `BackupTargetKind`, and the first destination the WebApi does not reach
through a filesystem. It mirrors to a **personal (non-Workspace) Google account's** My Drive. The
planner, the plan builder, the change detection, the state table, and the breaker are untouched —
the Phase 1 seams held. What Phase 2 adds is a **credential lifecycle** (a target can now be
*connected*, not just configured), an **ID-addressed destination** whose semantics are not a
filesystem's, and **batch economics** dominated by uplink throughput instead of disk bandwidth.

### Why user OAuth, and why `drive.file`

A service account — the usual server-integration path — cannot write into a personal Drive: service
accounts have **zero storage quota**, and a file uploaded into a folder shared from a personal account
is owned by (and charged to) the service account, so every upload fails. The only viable path is
**three-legged OAuth**: the app acts as the household's own Google account, and mirrored files land in
that account's My Drive, owned by it, against its quota.

The admin brings **their own Google Cloud project and OAuth client** — a self-hosted open-source app
has no server-side secret to hide and no single project Google could verify. Three facts about that
client belong in the setup guide in bold:

- **The client is deployment state, like the database volume.** `drive.file` visibility is keyed to
  the OAuth client: recreate it and the app permanently loses sight of every file the old client
  created. The existing mirror becomes an unmanageable orphan copy, and the next run uploads a full
  second one beside it.
- **The client secret is shown exactly once, at creation** (Google stores only a hash). Copy it then.
- **A client unused for six months is auto-deleted by Google.** A long-paused instance can come back
  to find its client gone.

The requested scope is **`drive.file` and nothing else**. It is classified non-sensitive, so the
consent screen publishes to production with **no verification review**, and it grants access only to
files the app itself created — sufficient by construction, since the mirror never touches anything
outside its own tree, and a genuine privacy line the connect UI states plainly: *Homestead can only
see and manage files it creates — never the rest of your Drive.* The consent screen **must be
published to "In production"**: Testing status auto-revokes refresh tokens after seven days, which
for a scheduled backup means a silent weekly death (detected — see the availability heuristics below).

### Connecting: the device authorization flow

The conventional redirect flow is deliberately **not** offered, for two reasons that compound:

- Google refuses non-HTTPS and raw-IP redirect URIs, which excludes plain-HTTP and LAN-IP
  deployments — a large slice of this app's real audience.
- The redirect callback is an unauthenticated cross-site request (the app cookie is
  `SameSite=Strict`), which demands signed-state, correlation-cookie, and PKCE machinery to stop an
  attacker splicing *their* Google account into the flow and receiving the library on the next run.

The **device authorization flow** dissolves both: *Connect Google account* on the target card shows a
short code and `google.com/device`; the admin consents on any device; the WebApi polls Google's token
endpoint until the grant lands. There is no redirect URI, no callback endpoint, and both in-app
endpoints (start, poll) are ordinary admin-authenticated same-site calls — the attack surface above
simply does not exist. The Google-side client type is "TVs & Limited Input devices". (`drive.file`
is on the device flow's restricted scope allowlist; re-verify at implementation time — the fallback
is a hardened redirect flow, not a design change.)

What the grant stores on the target-state row: the refresh token **encrypted via Data Protection**,
the connected account's email (the card reads "Connected as …" — which also answers "which account
did I use?" months later), and the token's issued-at timestamp.

**Disconnect is first-class.** It deletes the stored grant *and* calls Google's revocation endpoint
(best-effort) — as does replacing the grant on reconnect. Google does not invalidate old refresh
tokens when issuing new ones, so without revocation every reconnect strands a live credential. A
target whose config key has vanished still renders its card from the surviving state row, so
disconnect stays reachable — an orphaned grant with no surface would linger in the user's Google
security page forever. **Reconnecting a different Google account requires explicit confirmation and
resets the state table and folder map**: stored file ids are meaningless in another account's Drive,
so the old mirror is abandoned where it is and a full re-upload follows.

### Availability is composed, and lazily truthful

A local target computes its availability once, from config. A Drive target's availability also
depends on database state — is a grant stored, has it failed, does it still decrypt — so availability
becomes a **composed answer** (config verdict + target-state row) served by one service and consulted
at the same gates as before: run-now, schedule-upsert, the planner, the batch handler, and the list
endpoint.

Detection stays **lazy, but persisted**: the first operation to hit `invalid_grant` writes an
auth-failed marker (reason + time) onto the *target* row — a destination-level latch, because one
revoked grant affects every kind mirrored there. From then on the card reads unavailable-with-reason,
entry points refuse, and still-queued sibling batches short-circuit off the marker instead of
hammering the token endpoint in turn. Reconnecting clears it. A token dying **mid-run fails the run**
with the reconnect reason and never involves the delete breaker — it is an availability failure, not
a suspicious change set.

Three refinements keep lazy honest:

- **Decrypt failure is its own reason.** Self-hosters routinely recreate containers without
  persisting the Data Protection key ring; the stored token then fails to *decrypt*, which must read
  "stored credential could not be decrypted (data-protection keys changed) — reconnect", not
  masquerade as a Google-side revocation.
- **The seven-day heuristic.** An authorization that dies roughly seven days after its issued-at
  timestamp is almost certainly an OAuth app left in Testing status; the error says so, and says the
  fix (publish to production — no review needed for `drive.file`) instead of a generic "reconnect".
- **The badge is validated when someone is looking.** `GET /targets` stays a pure config+DB read, but
  after render the frontend fires an async per-Drive-target *validate* call — one refresh-grant probe
  that corrects the badge in place and trips the marker if the token is dead. And every backup row,
  all kinds, gains a **"last successful run: N days ago"** line — derived from `processing.jobs` like
  last-run status — styled as a warning past roughly twice the schedule interval. That line is the
  catch-all for every way a backup dies silently, not just auth.

### An ID-addressed destination

A Drive file's identity is its **id**; its name is metadata. `BackupObjectRef.Handle` carries that id,
exactly as anticipated. Everything below follows from names not being identity:

- **Writes address by stored handle.** A replace-`Put` and a `Move` go straight to `files.update` on
  the persisted id — a `Move` is a metadata patch (`name` + `addParents`/`removeParents`), one call,
  no re-upload (`SupportsMove = true`).
- **Names never address anything.** Drive legally holds duplicate names in one folder, `name =`
  queries are case-insensitive, and listing is eventually consistent. The only name lookup in the
  design is **crash recovery**: a handle-less `Put` (state row missing — a crash between the write
  and the row, or a re-created pairing) queries `(parent id, name)` — quotes and backslashes escaped,
  `trashed = false` — overwrites a deterministic match, and **trashes any surplus duplicates** rather
  than orphaning them. A lookup racing the search index can still miss a seconds-old file and create
  a twin; that twin is a tolerated orphan of the same class as the pre-release note above — the
  destination is never enumerated, so it is never noticed.
- **Every id-addressed update asserts `trashed: false`.** A no-op normally; a resurrection when the
  user has tidied the mirror into their trash. Without it, updates against trashed files *succeed*,
  and the card would show a green, converged mirror thirty days from being purged.
- **Verification uses the write response, not a download.** The upload response is asked for
  `sha256Checksum`, compared against `DocumentVersion.FileHash` (`SupportsContentHash = true` — the
  declared Phase 2 payoff). The field is documented "if available": absent, fall back to
  `md5Checksum` against an MD5 computed in the same pre-write verification stream; both absent,
  trust-and-record — the pre-write hash check has already guarded the content. `keepRevisionForever`
  is never set: unpinned prior revisions cost no quota and auto-purge (~30 days), so no revision
  cleanup exists; pinned ones would cost quota forever.
#### The target root

The root is a **two-segment path in My Drive** — `RootFolderPath`, default `Homestead/Backup` — created
by find-or-create, one `files.list` per segment, anchored at `parents: ["root"]` at the top and by id
thereafter. It is app-managed, and the setup copy says so: files placed there by hand are invisible to
the app (`drive.file`) and live at its mercy.

- **Two segments, not one flat folder.** The top segment is an app-owned namespace, so a future
  non-backup Drive context becomes `Homestead/Exports` rather than squatting a second top-level folder.
  Over a bare `Homestead`: the top segment would stop telling a human what they had found, and patching
  that with a `README.txt` at the root would force orphan detection to special-case a file no state row
  owns. `Backup` is title-case for consistency with `Documents`/`_versions`/`_undated` underneath — the
  whole premise is human readability, and Drive's case-insensitive name matching means the choice has no
  correctness dimension.
- **Created at connect time, not at first run.** The share-the-mirror workflow is the reason: you cannot
  share a folder that does not exist yet, and nothing else creates the root until a first run succeeds.
  Provisioning is a best-effort after-commit hook on both connect paths (first/same-account poll, and a
  confirmed account switch) plus the same idempotent call inline in `validate`, which the admin page
  fires per connected target on every load — so a hook that failed is repaired within a second. It never
  runs inline on the connect paths: the grant's flush is joined to the command transaction, and the token
  manager reads it from a separate connection, so it would see *not connected*. Its failures are
  swallowed by design — an after-commit throw would turn a connect that already committed into a 500.
  Accepted cost: an abandoned connection leaves one empty folder.
- **The resolved root is persisted and is the read path.** `backup.target_state.RootFolderPath` records
  the path as actually created, and `RootFolderWebViewLink` Drive's own URL for the folder. Config
  supplies the default on first creation; every run afterwards reads the database. That is what makes the
  admin card's location true *by construction* — under config-only, changing the value relabelled the
  card without relocating a single file, so the card could claim a location the mirror did not live at.
- **The path is a pin, and only an account switch clears it.** `DocumentBackupState` rows hold
  root-relative paths plus Drive file ids, so honouring a config edit after creation would mint a new
  empty root while every copied file stayed physically in the old tree — a mirror reporting converged and
  silently wrong. A **disconnect** therefore keeps it (it keeps the copy records too, which is what makes
  reconnecting the same account cheap), and so does the link, so the disconnect dialog's "the files stay
  in that account's Drive — delete them there" advice is one click away. A **confirmed account switch**
  is the one path that clears both, because it is the same path that deletes every copy record: new
  account, new ids, full re-upload, nothing left for the old path to keep coherent.
- **The chain above the root gets no map entry.** Only the last segment is remembered, under
  `backup.target_folders` path key `""`. Keys stay strictly root-relative, which is what keeps
  `PruneUpwardsAsync`'s stop-at-the-last-slash guard a *structural* guarantee that nothing above the
  effective root can be trashed — the failure mode being the user's own top-level `Homestead`. Admitting
  above-the-root entries would downgrade that to a rule someone has to remember. Cold cost of
  re-deriving them is one `files.list` per segment, once per process. The root's id is likewise **not**
  duplicated onto `target_state`: the two rows have different clearing rules, so a copy would be cleared
  in one place and not the other on the most common path.
- **`Homestead` is the most collidable possible top segment, and the collision cannot be detected.**
  `drive.file` cannot see a folder the app did not create, so a same-named folder of the user's own is
  invisible — it can never be adopted, and we cannot even warn that it exists. The mitigation is the
  link, not the name: the card renders the location as an anchor to the folder the app owns, which
  permanently answers "which of these two is Homestead's". A Google Picker would be the only sanctioned
  way to reach a user-*chosen* folder; it is rejected for now (see the non-goals) and the scope is not
  broadened.
- **Config-vs-actual skew is named, not silent.** Reading the location from the database fixes the false
  label but would otherwise turn a config edit into a no-op nothing acknowledges. When both values are
  present and differ (case-insensitively — Drive matches names that way), the card shows one line saying
  what config asks for, where the mirror actually is, and that reconnecting a different Google account
  starts fresh from config. **Deferred:** an *edit the folder* affordance, which forces an explicit
  relocate-vs-abandon decision plus real UX. The notice is what makes deferring it honest.

### Deletes trash; `files.delete` is never called

Drive's `files.delete` **permanently deletes, bypassing the trash entirely** — and on a folder it
permanently deletes all descendants the user owns, *including files the app's scope cannot even see*.
It is never called. Every delete in this target — file or folder — is `files.update {trashed: true}`.
The ~30-day trash window is the recovery net under every other mitigation in this section, and
trashing a parent trashes its descendants recoverable-in-place.

Two consequences are accepted and documented: trashed files **hold their storage quota** until purged
(~30 days), so a mirror-delete does not immediately free space — the out-of-space message tells the
admin that emptying Google's trash is how to reclaim it now; and the breaker's semantics are
unchanged — trash-not-delete is defense in depth under the breaker, not a replacement for it.

**Pruning empty folders is a final pass, not an inline step.** The local target prunes inline after
each delete/move; on Drive, an inline emptiness check races the eventually-consistent listing against
the same run's own moves — a folder could be trashed with a just-arrived file inside it, whose state
row would still read "mirrored" (a silent hole the planner never re-plans). The prune pass therefore
runs **after every mutation batch in the run has completed** — and that is now an enforced barrier, not
a convention. The sweep is tagged with the lowest stage, so it takes the lowest priority of any child in
the run, and the queue will not claim a child while a higher-priority sibling is still `Pending` or
`InProgress`. A continuation batch inherits its predecessor's priority rather than dropping below it, so
it too still sits above the sweep. The pass is trash-only and best-effort, and
walks up to the kind root exactly as the local rule does. Residual risk, accepted: a user file stored
inside the managed tree is invisible to the emptiness check and goes to the trash with a pruned
folder — recoverable, and the root folder's copy warns against doing this.

### The folder map and the ensure step

Drive folders are objects with ids, sibling folders may share a name, and name lookup is unreliable
by construction — so concurrent find-or-create is a race that mints twin year-folders and splits the
mirror permanently. Folders are therefore **resolved once and remembered**: a small state table maps
`(target, path) → folder id`, and a sequential **folder-ensure step** runs before batch fan-out. The
distinct folder set falls out of the plan (pure database work); ensure creates or verifies each
missing folder exactly once and writes the map; batches only ever read ids from the map and never
name-resolve. A 404 on a mapped id fails that action and invalidates the row; the next run's ensure
repairs it. Steady state costs zero folder calls, and a `Move` into a brand-new year shard works
because its folder exists before any batch runs.

Map keys are **strictly root-relative**, with `""` meaning the effective target root — the last segment
of `RootFolderPath`. Nothing above that root is ever mapped; see [The target root](#the-target-root) for
why that is load-bearing rather than tidy.

This is the one stated caveat to *the planner does no file I/O*: the ensure step is destination
metadata I/O, but it is bounded by the number of **new folders** (roughly, new year shards — tens),
never by file count, so it cannot threaten the job timeout the rule exists to protect.

### Deadline economics: budgeted batches, continuations, resumable sessions

The processor's hard five-minute per-job timeout was sized against local disk; against a residential
uplink it is the binding constraint on everything. Phase 1's 512 MB byte cap needs ~14 Mbps sustained
to fit; a metadata batch of 500 ops is 2–3 HTTPS round-trips each. Hence the per-target cap overrides
above — and one mechanism that makes the timeout a pacing device instead of a failure mode:

- **Batches are deadline-aware.** A batch stops starting new work with a safety margin left (~30 s),
  persists everything completed, and — if actions remain — **enqueues a continuation batch** under
  the same parent, then completes *successfully*. A large change set converges over a chain of green
  batches instead of a serial procession of `Failed` runs that make a working backup read as broken.
- **Large files upload in resumable chunks, and unfinished sessions survive the batch.** An upload
  that cannot finish inside the budget persists its resumable **session URI and offset** on the state
  row (sessions stay valid about a week); the continuation resumes mid-file instead of from byte
  zero. Without this, any file larger than one window's upload could *never* be mirrored — each
  attempt would restart from zero, forever. With it, anything up to Drive's per-file cap is
  mirrorable on any uplink.
- **Throttling is the target's problem.** 403/429 rate-limit responses get exponential backoff inside
  the target; the deadline awareness above means backoff consumes budget, not correctness. Google's
  current quotas are generous for a library-sized mirror; the documented **750 GB/day** upload cap
  applies to personal accounts but is far above any realistic run.

### Out of space is a typed outcome

Drive reports storage exhaustion, per-user throttling, and project throttling **all as HTTP 403**,
distinguished only by reason string (`storageQuotaExceeded` vs `userRateLimitExceeded` /
`quotaExceeded` — the last, confusingly, meaning *rate*). The classification is load-bearing:
misread a throttling storm as out-of-space and a transient blip skips a whole run's puts; misread
exhaustion as retryable and every put burns the full backoff ladder against a wall.

On genuine exhaustion the batch **fails fast for puts only**: remaining puts are skipped (they cannot
succeed), moves and deletes still run, everything that succeeded is persisted, and the run carries a
**coded out-of-space marker on the run summary** — a typed field, because the frontend does not parse
message strings and a parent job deliberately carries no error string of its own. The card renders it
as one message ("Google Drive is out of storage — mirror-deletes free space only after Google's trash
is emptied"), not two hundred identical fragments. Nothing latches: quota frees up outside Homestead
(the user empties trash, buys storage) with no event to observe, so the next scheduled run simply
retries.

Discovery moves earlier where it can: the validate call also fetches `about.get storageQuota`, so the
connected card shows **Drive free space**, and a dry run's summary is juxtaposed against it in the UI
("plan: 22 GB of puts; Drive has 4.1 GB free") — a display-time comparison, keeping the planner and
dry run free of file I/O as guaranteed.

### What the Drive target accepts, stated plainly

- **The library sits plaintext in a consumer Google account.** The local target's posture —
  "encryption is the storage layer's job" — has no Drive equivalent; the mirror is readable by
  anyone who compromises the Google account, and subject to Google's own access. Accepted for v1
  (client-side encryption would destroy the human-readable guarantee); the setup guide says so and
  recommends strong account security and 2FA on the connected account.
- **A database dump contains a working credential.** The Data Protection key ring is persisted in
  the same PostgreSQL database as the encrypted token, so at-rest encryption does not protect
  against a full DB compromise or a leaked dump — it protects against casual reads and partial
  access. Deployments wanting more must protect the key ring at rest (`ProtectKeysWith*`).
  Relatedly, a Postgres restore from an older snapshot resurrects whatever grant it held — possibly
  a revoked one, which lazy detection then surfaces on first use.
- **Tokens and codes never reach logs.** Token-endpoint responses and device codes are never logged;
  auth failures log the OAuth error *code* only.
- **Orphan classes are tolerated, never enumerated.** Crash-window twins that beat the search index,
  a mirror abandoned by a different-account reconnect, user files placed inside the managed tree —
  all consequences of the one-way guarantee, all recoverable by hand, none ever scanned for.

## Future-compatibility

The following must be addable without changing the planner or the run/job topology:

- **A cloud target (Phase 2)** — realized; see
  [Phase 2 — the Google Drive target](#phase-2--the-google-drive-target). The seams held as
  predicted: `RemoteHandle` carries the Drive file id and `SupportsContentHash` is real via
  `sha256Checksum`. The earlier claim that this needed *no* new tables proved half-true — the planner
  and topology were untouched, but the target-state row grew grant columns and a folder map was
  added: destination-shaped state, not plan-shaped, which is the distinction that actually matters.
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
  unmounted NAS mount point that still passes config-shape availability. Recorded, not built. (The
  Drive target's composed availability — lazy auth-failure latching plus the page-load validate
  probe — is the cloud analog of this idea, already built.)
