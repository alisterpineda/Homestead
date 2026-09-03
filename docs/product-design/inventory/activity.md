# Activity and notes — design

Companion to [overview.md](overview.md). This document covers the per-item **activity feed** — the single chronological stream on every item's detail page that interleaves system-recorded field changes with user-authored **notes** — and the note entity itself: what a note carries, how it is dated, edited, and deleted, and how the feed is filtered and sorted. It shares an interaction pattern and visual language with the Documents module's activity stream, and diverges from it where the "record of a physical thing" use case demands. The divergences are called out explicitly.

## Purpose

A household keeps things that accumulate a story: the car gets its oil changed, the furnace filter is swapped, the laptop goes in for a screen repair, the bike gets new tyres. Inventory already answers *where is it* and *how much is left*; the activity feed answers *what has happened to it*. The canonical use is a **maintenance and repair log** on a durable, but the same surface takes any dated remark — "started making a grinding noise", "warranty registered", "switched to the Kirkland brand, noticeably worse".

This is deliberately **not** a maintenance scheduler. Notes record what *was* done; nothing here computes what is *due*. Reminders and next-due dates belong to a future Calendar/Maintenance module. What v1 does capture so that module can be built without a backfill is the **subject** of each note — "Oil change", "Furnace filter" — because "when was X last done" is only answerable if X was recorded as its own field, not buried in prose (see *The note*).

## Conceptual model

Each item has exactly one **activity feed**. It is a read-time merge of two sources:

- **Audit entries** — system-generated, one per field-level mutation of the item, projected from the shared `auditing.audit_logs` table into a human-readable summary ("Alister updated location"). These already back the durable detail page's History section; nothing is double-written.
- **Notes** — user-authored, dated entries stored in their own table (`inventory.item_notes`), keyed by `Item.Id`.

The two are interleaved in one chronological list, as Documents interleaves audit entries and comments. There is one source of truth for "what changed" (the audit log) and one for "what the household said about it" (notes); the feed is the projection of both.

Three existing concepts stay distinct from the feed, and the design keeps them so:

| Concept | What it is | Relationship to the feed |
|---|---|---|
| `Item.Notes` (free-text field) | A *standing* description — "keys are in the glovebox" | Unchanged as data and unchanged on the wire (the API property stays `notes`). Relabelled **Description** everywhere it is shown or edited, so "note" unambiguously means a feed entry. Edits to it appear in the feed as an audit row ("updated description"). |
| Movement ledger (consumables) | Typed quantity history, queried and summed | Stays its own section on the consumable detail page, retitled **Movements**. Never merged into the feed. This holds because the stock service audits quantity changes under the `StockEntry` entity type, not `Item`, so they never reach the item projection. |
| System audit log | Field-level who-changed-what, all modules | The feed *is* its projection for this item, filtered to `EntityType == "Item"`. Audit rows about a note itself are excluded (see *Auditing notes*). |

## The note

A note is a dated, user-authored record on one item. It is intentionally small.

| Field | Rule |
|---|---|
| `Id` | uuid PK. |
| `ItemId` | FK to `inventory.items`, **cascade** on delete. The delete *gate* is enforced in the delete handlers, not by the FK (see *Delete gate*). Indexed with `OccurredAt`: `(ItemId, OccurredAt)`. |
| `OccurredAt` | `DateTimeOffset`, UTC. **When it happened**, not when it was typed. An untouched date in the composer stores the full current instant; an explicitly picked date pins to **12:00 UTC**, the Finances rule for date-only entries. **May not be in the future** — the same guard the stock service applies to movements (`UtcNow + 1 min`). |
| `Subject` | Optional, single line, max 200. "Oil change", "Emissions test", "Roof rack". The feed headline when present, the filter target, and the task identity a future reminders module keys on. The composer autocompletes it from the item's own distinct prior subjects, so the second "Oil change" snaps to the first and spellings converge. Trimmed; blank stores null. |
| `Body` | Multi-line plain text, max 4000. **Required unless `Subject` is set** — a note must say *something*, but "Oil change" alone is a complete entry. |
| `CreatedAt`, `UpdatedAt`, `CreatedByUserId`, `UpdatedByUserId` | Standard attribution; `SetNull` on user delete; display only, never access control. |

What is deliberately **not** on the note, and where it goes instead:

- **No kind / category enum.** A fixed kind (Maintenance / Repair / …) would repeat the Category mistake this module already corrected, and every use it had is covered better by `Subject` plus the text filter. If a behavioural classification is ever needed, a `Kind` column with a default is additive.
- **No cost.** Money lives in Finances, which already carries amount, currency, date, payee, category, and the receipt via document links. A later **note → transaction link** (see *Future-compatibility*) attaches that record to the note; storing a second amount here would duplicate it with none of that context, and spend reporting is a Finances query, not a feed header sum. An amount typed into the body is fine as a remark; it is not data.
- **No performed-by.** "At Midas" belongs in the body.
- **No odometer / reading / next-due.** A meter reading is a point-in-time attribute of the entry, and is the first customer of a future generic attribute system that attaches to both items and notes. The rule for what earns a column: **only values the system reasons about** (today: when, what subject, who wrote it). Everything it merely displays is body text now and an attribute later.
- **No title.** `Subject` is the headline; a separate title would be a second copy of it.

### Which items can carry notes

**Both kinds.** The feed component is shared, and a consumable note ("switched brand", "this batch is defective") is a real use. Notes can be added to an item in any status, including terminal ones — "sold to Sam" is a good final entry on a sold item. An archived consumable's quick panel is read-only, but its detail page still shows the feed and accepts notes.

### Notes are editable and deletable

Documents comments are immutable in v1 (edit/delete is listed there as future work). Inventory notes **ship editable and deletable from the start**: a repair record with the wrong date has to be fixable, and an accidental note has to be removable. There is no time window or author restriction — any contributor can edit any note (household-global, like everything else in Inventory). Deleting a note is a real delete (no trash, no undo), consistent with the cross-cutting stance.

Because notes are the first multi-user-editable free text in Inventory, last-write-wins on concurrent edits is an accepted loss (no optimistic concurrency token, matching the module's existing stance); the paragraph is small and the edit is repeatable.

Two consequences are surfaced rather than hidden:

- An edited note shows an **"edited"** marker after its time whenever `UpdatedAt` differs from `CreatedAt`; hover/long-press reveals who and when. Any contributor can rewrite anyone's note, so the row must not silently keep the original author's name over changed text.
- A deleted note leaves **no user-visible trace** in the feed. The only record is the audit row (below), which stores the note's subject and first line as its description — the audit logger records a description on delete, not a field diff.

### Auditing notes

Every note create / update / delete is written to the audit log via `IAuditLogger` under **its own entity type, `InventoryNote`**, with `ParentEntityId = ItemId`. The precedent is `DocumentComment`, which Documents audits under its own type with the document as parent and does *not* project into the document feed (Documents *does* project `DocumentVersion` rows through `ParentEntityId` — that is the deliberate counter-example, because a version change is a change to the document). Note audit rows are **not** projected into the item's feed: "Alister edited a note" beside the note itself is noise, and a "deleted a note" ghost would contradict delete-is-destructive. The feed shows the note in its current state, or not at all.

The entity-type string is chosen to be **subject-neutral** (`InventoryNote`, not `ItemNote`) because audit rows are never re-migrated: if notes later gain a second subject kind (see *Future-compatibility*, areas), the history written today must still read correctly.

## The feed

### Sorting

The merged list is ordered by a single **effective date**: `OccurredAt` for notes, `Timestamp` for audit rows — newest first, then `CreatedAt` (notes) / `Timestamp` (audit) as the second key, then `Id` as the final tiebreaker so equal instants across the two ID spaces are stable and never drift across page boundaries. Because notes sort by when they *happened*, a back-filled 2019 oil change on a car added to Inventory in 2026 sits **below** "added this item". That is correct: the feed is the story of the object, not of the database row — the same stance as *status is the state of the thing, not the row*.

One caveat the doc states because a user will notice it: the two halves are on different clocks. A note is dated by the user; an audit row is dated by when the edit was *recorded*. Flip a laptop back to Active a week after the repair note says it came home and the status row sits above the repair. Audit rows therefore read as "recorded" events, and the row copy keeps the existing "{summary} · {age}" form rather than implying an occurrence date.

### Filtering

The section header carries:

- An **All | Notes** segmented toggle. "Notes" hides audit rows. The default is **Notes whenever the item has at least one note, otherwise All**, so a fresh item still shows its audit trail and a maintained item opens on its log. The last choice is **persisted per user** as a preference, the same mechanism as the Items list's table/cards view mode. This is a deliberate divergence from Documents, whose feed is read as a conversation; a service log is scanned like a table.
- A **filter box** that matches against `Subject` and `Body` (case-insensitive contains) and offers the item's distinct subjects as suggestions. "When did I last change the furnace filter" is typing "filter". The filter applies to notes only; when it is non-empty the toggle is forced to Notes.

Both are per-page state except the toggle's persisted default. Changing either resets to page 1.

### Pagination

Both sources merge and paginate in memory, as the durable History feed already does; household-scale volumes make this the right trade. Page size 20, with the durable page's existing prev/next pager. The consumable detail page keeps a **separate** page signal for the feed, distinct from its Movements table's pager. The Notes-only and filtered views paginate over their own result set.

### Empty state

"No activity yet." Because every item is created through an audited command, a freshly created item has one audit row, so on All the empty state is unreachable in practice. On Notes: "No notes yet." With a filter: "No notes match."

## UX

The feed replaces the **History** section on the durable detail page and is added to the consumable detail page (which today has no audit feed; its movement ledger is retitled from "History" to **Movements** to make room). It is the **last section on both pages**, keeping the shared detail-page anatomy described in the overview.

### Shared language with Documents

- **Audit rows** are quiet: a muted history icon, extra-small muted text, `{summary} · {relative time}`.
- **Note rows** are prominent: a small round icon bubble, then the author's name and the note's time on the first line, then the content. Content is `Subject` in medium weight when present, and `Body` beneath with whitespace preserved.
- **The composer** is a single group: a **Subject** line with autocomplete, a **Body** textarea, and a send button. Placeholder on the body: "Add a note…". Enter in the subject line moves to the body; **Enter in the body submits, Shift+Enter inserts a newline**. A one-line remark is: type, Enter — the Documents gesture.

### Divergences from Documents, and why

- **The container.** Documents' stream is a pinned pane in a fixed-height tab with the composer at the foot, always on screen. Inventory's detail pages are one long scroll, so the composer sits **directly under the section header, above the list** (the list is newest-first, so the newest note appears right beneath where it was typed). Today's bordered `<ul>` audit list is restyled to the Documents unbordered stream, wrapped in one bordered card for section parity.
- **Multi-line input.** Documents uses a single-line `<input>`. Inventory uses a textarea with `field-sizing: content`, one row growing to about five, because a service note is often a paragraph. Bodies longer than three lines **clamp** in the feed with "Show more"; a forty-note log has to scan.
- **The time shown is `OccurredAt`.** Documents shows creation time. Here the headline time is when it happened, rendered relative within the last week and as an absolute date beyond that, so a back-filled entry never reads "2 minutes ago" below rows reading "3 years ago".
- **The date chip.** Beside the send button sits a small date chip, defaulting to today. Tapping it opens a native date picker; future dates are rejected. During a session the chosen date is **sticky** across submits and is rendered on the chip, so back-filling fifteen receipts is fifteen date changes and nothing else — "today" is the plausible-looking wrong date nobody notices, while a stale carried-forward date is visibly stale. This mirrors the create dialog's "Create & add another", which keeps kind and location for batch entry.
- **Edit and delete.** Each note row has a `⋮` overflow with **Edit** and **Delete**. Edit swaps the row for the composer pre-filled (subject, body, date), with Save / Cancel; Escape cancels; only one composer is active at a time, so the header composer is disabled while an edit is open, and the edited row is scrolled into view. Delete confirms.
- **Filter box and toggle** in the header, described above.

### Responsive rules

Both detail pages are single-column with no tablet/desktop branch, so the rules are the same at every width: the section header wraps (`flex-wrap`), with the toggle and filter box dropping to their own line below the title at narrow widths; the composer stacks subject over body with the date chip and send button on a trailing row; the note row's `⋮` stays reachable at the row's trailing edge. Nothing in the feed introduces a second horizontal scroll region.

### Where the feed does *not* appear

- **Quick panels.** The panel is a fast read-and-adjust surface with a nothing-stacks rule; a two-field composer is a real form. Not in v1. A read-only "Last note: {subject} · {date}" line in the footer meta strip is a plausible follow-up now that `Subject` exists.
- **Items list.** No column and no facet. A "last serviced" column derived from the newest note with a subject is plausible later and additive.
- **Browse.** Nothing.
- **Universal search / ⌘K.** Note subjects and bodies are not indexed in v1 (same stance as brand and tag names). Additive later via the `ISearchSource` seam.

### Status coupling

Marking a durable `InRepair` → `Active` (or `Active` → `InRepair`) does **not** auto-create a note in v1; the status change already lands in the feed as an audit row. A "log this repair" hand-off from the status form is a natural follow-up once the composer exists, but it must not make the quick panel stack, so it is deferred.

## Delete gate

Notes are user-authored history and join the existing rule that *delete is the mistake-eraser and archive is for things with history*. The full gate after this change:

- A **durable** cannot be hard-deleted while it has child items (stored-in contents or components), holds fungible stock (a stock entry points at it as a container), is referenced by a `Conversion` movement (it was tracked out of a consumable), **or has any note**.
- A **consumable** cannot be hard-deleted while it has ledger history **or any note**.

The gate is enforced in the delete handlers with a business-rule error naming the reason: "Item has notes and cannot be deleted. Delete its notes first, or archive it instead." The FK itself **cascades** (matching `DocumentComment`), because notes are freely deletable by any contributor and a Restrict FK would add no protection — a user can delete the notes one by one and then the item — while turning any future bulk/purge path that skipped the handler check into a raw 500. The gate is a friendly guard on the mistake-eraser, not a durability guarantee; archiving remains the way to retire an item and keep its story. The static copy in both pages' delete confirmation dialogs is updated to name notes alongside the existing reasons.

## Audit

Note mutations are audited (see *Auditing notes*). The item-level audit rows that already exist are the feed's other half and gain no new fields. The `Notes → Description` relabel is presentation only: the persisted column, the API property, and the audit field name stay `Notes`; the activity summary label changes from "notes" to "description" in the display-name map, so historical rows read consistently with the current UI. The relabel covers every surface that shows or edits the field — the create dialog's "More details", both edit sheets, the Details card on both detail pages and the durable quick panel (which today render the text as an unlabelled paragraph and gain an explicit "Description" label).

## Implementation scope beyond the feed

Listed so the plan does not miss them:

- The durable activity endpoint returns null for consumables and its display-name map is durable-shaped; it is generalised to both kinds (or replaced by an item-level endpoint) with the consumable field labels (`Unit`, `RestockThreshold`) already present in the map. Consumable mutations are already audited under `Item`, so the projection works without new writes.
- The API resource for notes is named to avoid the `notes` property already on the item responses (e.g. `/inventory/items/{id}/activity` for the feed, `/inventory/items/{id}/activity/notes` for note CRUD).
- Cross-references updated: the consumable "History" table is now **Movements** (a locations.md aside names it), stock.md's delete rule adds notes to the gate, and the overview's detail-page anatomy names Activity.

## Future-compatibility

Additive on top of `inventory.item_notes`, none requiring a re-migration:

- **Note → Finances transaction link** — the cost of a repair as the actual expense, with its payee, category, currency, and receipt. A join table keyed `(NoteId, AccountTransactionId)`, cascading from both sides, ID-first with reads through the Finances query service, following the transaction–document link precedent. Spend-per-item and spend-per-year then become Finances queries over linked transactions. Deleting a note with links must name them in the confirmation.
- **Note → Documents link** — the invoice or manual page, same shape as the transaction–document link (composite key, cross-schema FK cascading from the document, no navigation property). The reverse view ("referenced by") does not exist for Finances either and would be a shared Documents-side seam.
- **Generic attributes** on items *and* notes — the odometer reading at a service, filter size, tyre pressure. Designed once, attached to both. Until then the note table never grows domain-specific columns.
- **A `Kind` column** if a behavioural classification ever earns one. Additive with a default. A free-form classification, if wanted instead, reuses the inventory Tag tree on notes rather than a second taxonomy.
- **A subject lookup.** If reminders need canonical task identity, distinct `Subject` values promote mechanically into a lookup row — a cheap backfill, unlike re-reading prose.
- **Quick-panel "last note" line**, **"last serviced" list column**, **status → note hand-off**, and **note text in universal search**, all described above.
- **Next-due / reminders**, in the Calendar/Maintenance module, keyed on `Subject` for "last done".
- **Sync.** Note delete is a hard delete with no tombstone; the `InventoryNote` / `Deleted` audit row is the only trace a future sync design would have to work from. The overview's sync note is worded accordingly.
- **Documents comment editing** adopting the same **interaction pattern and row/composer styling** — explicitly *not* a shared component or entity shape, since comments carry no `OccurredAt` or `Subject` and cascade rather than gate.
