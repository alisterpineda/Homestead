# Inventory — Requirements

This document describes the Inventory module within Homestead: what it is, what it does, what it does not do, and the design decisions that shape it. It is product-level, not implementation-level — see the repo's CLAUDE.md files for code structure and framework choices.

Inventory sits alongside Documents and Finances and the cross-cutting infrastructure modules listed in the [product-design index](../README.md). This file governs *what* the module should do; defer to existing module patterns in the codebase for *how* it is wired up.

## Purpose

The Inventory module is the household's record of everything it owns and where each thing physically lives. It answers two questions reliably: *"where is X?"* and *"how much of Y do we have left?"* — the first for individually-tracked possessions (a drill, a passport, a camera body), the second for consumables kept in bulk (screws, paper towels, batteries). The primary outcome is that a household member can find a specific item without remembering where they put it, and can tell at a glance which consumables are running low before they run out.

The module is not an asset-management system, a warehouse/WMS, a shopping-list app, or a maintenance scheduler. It captures what you own and where it is, and tracks consumable quantities over time; it does not manage procurement, depreciation schedules, or service calendars. Those are deferred or belong to other modules.

## In scope for v1

- **Areas** — a flat set of named rooms or zones (Garage, Home Office, Pantry) that anchor physical location.
- **Items** — anything owned, in one of two flavours distinguished by **fungibility**:
  - **Non-fungible** items: single, identity-tracked instances that live in exactly one place (or nowhere yet).
  - **Fungible** items ("consumables"): a *type* whose quantity is spread across one or more locations, with a total on-hand that is the sum.
- **Holding** — any non-fungible item can hold others, as a **container** (storage: a place you browse into) or an **assembly** (composition: a machine and its components); both nest arbitrarily deep, and moving one carries its contents/components automatically. A child's relationship is derived from the parent's kind (`DurableKind`).
- **Derived location** — an item's resolved area is computed by walking up the parent chain through holders of either kind, never stored, so move-with-contents is free and never drifts out of sync.
- **Stock tracking** — per-location quantities for fungible items, a total on-hand, and a low-stock signal against an optional per-item restock threshold.
- **A usage/movement ledger** — an append-only history of every fungible quantity change, so consumption over time (burn rate) can be measured. Captured from day one even though v1's surfaces are mostly current-state.
- **Lifecycle status** — items carry a status (`Active`, `Lent`, `InRepair`, `Sold`, `Discarded`, `Archived`) that records the real-world state of the thing, with terminal states hidden from default views.
- **"Track this one individually"** — carve a single non-fungible instance out of fungible stock (and the reverse), losslessly and atomically.
- **Estimated value** — an optional per-item value in a chosen currency, for insurance/rollup, captured but not yet aggregated.
- **Brand and model** — an optional **brand** (a canonical, shared lookup applied to *both* durables and consumables, and the axis for "show me all my Apple devices" grouping) plus, for durables only, an optional free-text **model** designation sitting alongside the serial. Both round out the make/model/serial identity cluster of a manufactured good.
- **Tags** — a hierarchical, multi-valued classification applied to *both* kinds, answering "what kind of thing is this / what cross-cutting groups does it belong to." Tags are the item classification axis and **replace the deferred single-valued Category taxonomy**: path-delimited identity, colour inheritance, a dedicated CRUD page, inline find-or-create on the item forms, and a descendant-expanding filter facet on the Items list. (See [tags.md](tags.md).)

## Out of scope for v1

- **Photos / attachments** on items — important for non-fungible items and insurance, but deferred to its own table. (Planned, see *Future-compatibility*.)
- **Barcode / QR scanning** and **printable container labels** ("scan a bin → see its contents").
- **Expiry / perishables and per-category custom fields** (food best-before dates, medication, filter service dates) — these feed reminders and belong with a future Calendar/Maintenance surface.
- **Per-location restock thresholds.** v1 has a single global threshold per fungible item, compared against the total on-hand.
- **Value rollup and currency conversion.** Item values are captured in their entered currency and shown as-entered; aggregating them into a household total or converting into a display currency is a Finances-integration concern.
- **Cost reporting.** Per-purchase unit cost is captured on the ledger but no spend/cost report is built in v1.
- **Offline / sync.** No offline-specific code. The model keeps the door open cheaply (uuid PKs, delta-based quantity changes through the ledger rather than absolute overwrites, status-based lifecycle rather than hard deletes) but merge/conflict resolution and client-side ID generation are deferred.
- **Cross-module links** — receipts/warranties/manuals (Documents), value rollup (Finances), consumables → maintenance schedules and expiry reminders (a future Calendar/Maintenance module). `Item.Id` is kept stable as an FK target so other modules can reference items directly when those links arrive.
- **Multi-user permissions / per-item ownership.** Inventory data is household-global, like Documents and Finances.

These are excluded from v1 but the model must not block adding them later. See *Future-compatibility*.

## User-facing concepts

### Area

An **Area** is a fixed structural location — a room or zone the household thinks of as a place: "Garage," "Home Office," "Pantry." Areas form a **hierarchy** (`House › Basement › Bedroom`) via an optional parent area; a flat list is just the case where every area is a root. An item or a unit of stock is ultimately *in* a leaf area, either directly or by sitting inside a container that is in that area — and that placement **rolls up**: it counts toward the leaf area and every ancestor above it (something in Bedroom counts toward Bedroom, Basement, and House). An area carries a name and an optional parent, and can hold sub-areas and its own direct items/containers at the same time. Deleting an area is blocked while it has sub-areas, or while any live item or stock currently sits in it; re-parenting an area moves its whole subtree with it (resolution is derived, so nothing cascades), guarded only against cycles. Historical references to it from the movement ledger survive a deletion (the recorded quantity and what happened to it persist; only the now-meaningless location label is cleared).

See [locations.md](locations.md) for the area model, the area hierarchy and roll-up, the containment tree, resolved-location derivation, move-with-contents, re-parenting and the cycle guard, the Unplaced state, and area lifecycle rules.

### Item and fungibility

An **Item** is anything the household owns. Every item is one of two kinds, set by a neutral **fungibility** discriminator that is fixed for the life of the row:

- A **non-fungible** item is a single, identity-tracked instance — *this* drill, *this* passport. It lives in exactly one place at a time, or nowhere yet ("Unplaced"). It can carry an estimated value, a serial number, a **model** designation, a free-text compartment note ("top drawer"), and an **acquisition cluster** — how it entered the household (`AcquisitionKind`), when (`AcquiredOn`), what was paid (`AcquisitionCost` + currency, a historical fact distinct from the estimated value), and from whom or where (`AcquiredFrom`, free text). `AcquisitionKind` is deliberately four-valued: one behavioural value (`Purchased` — the only kind where a price is expected), two provenance values (`Gift`, `Inherited`), and a catch-all (`Other`); null means "not recorded", the normal state for a backfilled household. Like any item it can also name a **brand** (see below).
- A **fungible** item is a *type* of consumable — screws, paper towels, printer paper — measured in a unit ("screws," "rolls," "gal"). It holds no single location; instead its quantity is spread across one or more **stock entries**, one per location, and its total on-hand is their sum.

The labels users see — "durables" vs "consumables" — are presentation only. The persisted discriminator is the neutral `Fungibility` value, so the wording can change without touching data. Fungibility never flips on an existing row: converting between the two kinds is always done by creating or retiring rows (see *Track this one individually*), never by toggling a flag, which keeps each row's location and stock columns coherent.

See [stock.md](stock.md) for the fungible model in full: stock entries, the movement ledger, thresholds, and the conversion operations.

### Brand

A **Brand** is a canonical, first-class lookup entity — a consumer manufacturer name ("Apple," "Makita," "Sony") an item is attributed to. It is deliberately *not* free text and *not* a tag: it is a single shared row that both durables and consumables can reference through one optional foreign key on the item, so a brand is single-valued per item and applies to **both** kinds. Brand is the axis for the grouping question "show me everything I own by this maker," surfaced in v1 as a single-select **filter facet** on the Items list (matching the Area facet) and as a click-through chip on each item's detail — not as a list column. "Brand" is the label because the grouping people want is by *consumer* brand, not the OEM that actually built the part.

Brands are created by **find-or-create at entry time**: a combobox on the item forms autocompletes existing brands and offers "Create 'X'" for a new name; creation is idempotent and case-insensitive, so typing "apple," "Apple," or " apple " all resolve to the one canonical row (a database unique index on the lower-cased name is the backstop). A dedicated **Brands management page** (`/inventory/brands`, in the sidebar's "Manage" group beside Items / Areas / Tags) owns deliberate **rename**, **delete**, and **merge** — the surface entry-time find-or-create can't provide. A brand that is **in use cannot be deleted** (a friendly in-use guard ahead of the FK restrict backstop), so an attributed history never dangles; **merge** is how you retire an in-use brand, re-pointing every item off the source onto a target and then deleting the source. Merge is also the only way to consolidate *true* duplicates ("Apple" / "Apple Inc," differing by more than casing) — a plain rename onto an existing name hits the unique index (a 409), which the rename dialog turns into an inline "merge into it?" handoff. See the [Brands deep-dive](brands.md). Brand is intentionally *not* wired into full-text item search or the ⌘K palette in v1 — it is a discovery facet only, additive later.

### Tags

A **Tag** is the item **classification** axis — "what kind of thing is this / what cross-cutting groups does it belong to" — and the one classification that is **multi-valued**. Tags **replace the deferred Category taxonomy**: instead of a flat single-valued category lookup, items are organised by a **hierarchical**, path-delimited tag tree (`Electronics/Batteries/AA` is one tag whose path is its identity, upserting its ancestors on write). Both durables and consumables carry tags, through a many-to-many join. Where Brand answers "who made it" (single-valued, shared lookup) and Area answers "where is it" (structural place), Tags answer "what is it / what groups is it in" — the three are orthogonal.

Tags are a faithful port of the Documents tag subsystem: each tag has an optional user **`Color`** and a materialised **`EffectiveColor`** that a parent's colour cascades into every uncoloured descendant; editing or deleting a tag repairs that cascade in one pass. Unlike Brand, tags have a **dedicated CRUD page** (`/inventory/tags`, in the sidebar's "Manage" group) for create / rename / recolour / re-parent / delete (single and bulk), *and* **inline find-or-create** on the item forms (a path-based chip input that upserts ancestors, full parity with the Documents tag input). Deleting a tag is **destructive with no in-use block**: it un-tags every item, re-parents the tag's children, and repairs the colour cascade — items are never deleted, only un-tagged. Like the rest of Inventory, tags carry **no access-control guards** (household-global). Tags surface as a **descendant-expanding multi-select filter facet** on the Items list (an "Include sub-tags" toggle expands each selected tag to its subtree) and as colour badges on list rows, quick panels, and detail pages; a badge click on a quick panel or detail page deep-links to the tag-filtered list (the dense list-row chips are read-only, since the row is the click target). Tags are intentionally *not* wired into full-text item search or the ⌘K palette in v1 — a filter facet only, additive later (like Brand).

See [tags.md](tags.md) for the path model, colour inheritance and cascade, the filter expansion, inline find-or-create, and create/edit/delete semantics.

### Holders: containment vs composition

Every non-fungible item carries a three-valued **`DurableKind`** that names its holder-role: **`Plain`** (holds nothing — the default), **`Container`**, or **`Assembly`**. Being a holder is a role any non-fungible item can play, not a separate type — a toolbox, a drawer, a shelf, and a storage bin are all just items with `DurableKind = Container`. The two holder kinds model two genuinely different relationships:

- A **container** is a storage *place* you browse into and file loose things under. Its contents are the non-fungible items parented to it **plus** any fungible stock whose stock entry points at it (shown at the per-container quantity). Containers nest: a bin inside a cupboard inside the garage.
- An **assembly** is a machine that holds **components** — a CPU in a PC, installed not stored. An assembly is **not a place**: it never appears as a drillable card in spatial Browse and cannot take loose stock. Its components are still **first-class items** in the unified list (their location reads "Part of Gaming PC") and resolve to the assembly's area, but they are reached through the assembly's detail-page **Components** section, not the area's Browse drill-in.

A child's relationship is **derived from its parent's kind** — there is no per-child attachment flag. The free-text `Compartment` doubles as the component **slot label** ("CPU", "RAM slot 1"); structured/empty slots are deferred. Fungible items are never holders (only a single, identity-tracked thing can hold contents), so `DurableKind` is null for them. A holder's kind cannot be changed while it still holds anything.

This replaces an earlier `IsContainer` boolean, and fixes its conflation bug: selling a PC as a unit is no longer blocked by its live components (they ride along, like the contents of a lent bag), whereas a container with live stored-in contents still blocks.

See [locations.md](locations.md) for containment-vs-composition typing rules, nesting, cycle prevention, and what "empty" means.

### Resolved location

Where an item *is* — its resolved **area** — is always derived, never stored on the item. For a non-fungible item directly in an area, that area is the answer. For an item inside a container, the answer is found by walking up the parent chain to the first item that sits directly in an area. An item (or a chain) that never reaches an area resolves to **Unplaced** — a first-class state, never silently defaulted, surfaced as its own bucket when browsing by area.

This derivation is the reason **moving a container is free**: changing only the container's own location updates the resolved area of everything inside it — every nested item and every unit of stock referencing it — with no cascade and no stored value to keep in sync. Denormalising a resolved area onto nested rows is explicitly disallowed; it would reintroduce exactly the move-with-contents drift this model exists to avoid.

See [locations.md](locations.md) for the walk-up algorithm, the Unplaced bucket, and move semantics.

### Stock and consumables

For a fungible item, **stock** is the per-location breakdown of how much is where. Each **stock entry** records a quantity at one location — an area or a container. The item's **total on-hand** is the sum across its entries, and a fungible item is **low** when its total is at or below its **restock threshold** (when one is set — thresholds are optional). The threshold is global to the item in v1, compared against the total, not against any one location.

Users see consumables two ways, both inside the unified **Items list** (durables and consumables are one list with an `All | Durables | Consumables` kind filter): globally, one row per fungible type with its total on-hand, its locations, and low-stock surfaced (so consumables don't require digging through rooms) — the list's area filter narrows this to a chosen area, still showing each type's total; and in-context when drilling a place in **Browse**, where a consumable row shows the local quantity sitting there with an "of N total" hint. Per-location quantity controls let a user record using some, transferring stock between locations, or correcting a count.

Day-to-day changes happen in a **quick panel**: clicking a consumable row opens a side panel over the list showing the total, the per-location breakdown, and − / + steppers per location, with move/correct/assign-to-a-place as inline expansions. Stepper bursts are coalesced into single ledger movements (see [stock.md](stock.md#one-movement-per-gesture-not-per-click)). Both kinds have a **detail page** at `/inventory/items/:id` (see point 9 for the shared anatomy): the consumable detail page is the home of the full movement history and the conversion operations (*Track this one individually* and *Collapse*), while the durable detail page is the home of its resolved location, details, contents, and activity history.

See [stock.md](stock.md) for stock entries, the unique-per-location rule, thresholds, and the consumables-list UX.

### Stock movements and usage

Every change to fungible quantity is recorded as one row in an append-only **movement ledger**, written in the same transaction as the quantity change itself. The ledger is what makes *consumption over time* measurable: how fast the household goes through a consumable, for reorder timing.

A movement's **direction** is read from its location fields — stock coming in (a destination, no source), going out (a source, no destination), or moving internally (both source and destination, which nets to zero). On top of direction, each movement carries a small **kind** that separates real flow from bookkeeping:

- **Flow** — a genuine acquisition or consumption (bought 3 rolls; used 1). These are what usage analytics count.
- **Adjustment** — a recount or correction that reconciles the recorded number to reality. Excluded from usage so a correction never distorts burn rate.
- **Conversion** — the quantity moved because an item was carved out individually or collapsed back (see *Track this one individually*), not because anything was bought or used. Also excluded from usage.

Consumption, then, is precisely the Flow movements that leave a location without a destination; transfers and bookkeeping fall out automatically. This ledger is **distinct from the system audit log**: the audit log records under-the-hood field-level "who changed what" across every module and is never aggregated, while the movement ledger is typed domain data that is queried and summed. Both are kept — the audit log keeps recording metadata edits (renames, threshold changes), the ledger owns quantity history.

See [stock.md](stock.md) for the ledger shape, the direction/kind model, the usage query, and how the conversion operations write it.

### Status and lifecycle

Every item carries a **status** that records the real-world state of the physical thing, not the state of the database row:

- `Active` — owned and in use/storage. The default.
- `Lent` — lent out, with a free-text note of *to whom* (no contacts graph in v1). The note travels with the item wherever it is listed — beside the status badge in the unified list and on the overview's lent tile — so "with whom" never requires opening the item.
- `InRepair` — temporarily out for service.
- `Sold` / `Discarded` — gone from the household. Terminal.
- `Archived` — retained in records but not part of the active picture. Terminal.

Status is **domain lifecycle, not soft-delete.** Terminal statuses (`Sold`, `Discarded`, `Archived`) drop out of default lists but are revealed by an explicit "Show archived/inactive" filter — the same idiom Finances accounts use for archiving, not a global hidden-row filter. A real delete still exists for genuine data-entry mistakes, and follows the Finances rule: delete is disabled once an item has history that should outlive it (contents, or ledger movements). Fungible consumables use a reduced status set — only `Active` and `Archived` apply, since lending or repairing a *count* of screws is meaningless.

See [locations.md](locations.md) for the full lifecycle: terminal-status handling, what happens to a sold item's location, container-emptiness rules, and delete-vs-archive.

### Track this one individually (and the reverse)

A household often keeps something in bulk and then wants to track one unit on its own — pull one bin from a case of twelve and start logging it individually. **"Track this one individually"** does exactly that, atomically: it decrements the fungible stock by one at that location, writes a `Conversion` movement recording the change, and creates a non-fungible item at the same location, linked back to the consumable it came from. Nothing is lost and the books stay balanced — it is a normal "start tracking this one" action, not a schema conversion. It is offered only when the source location holds a whole number of units (carving "one" out of 2.5 kg of flour is meaningless) — a whole-number on-hand is the proxy for "count-like", with no separate per-consumable flag.

The **reverse** — collapsing several individually-tracked instances back into a fungible count — is mechanically simple (+N to stock, written as N quantity-1 `Conversion` inflows, one per instance) but **lossy**: per-instance serial numbers, values, and history have nowhere to go. So it warns, and it **archives** the collapsed instances rather than hard-deleting them, so their records survive.

See [stock.md](stock.md) for both operations step by step, including the whole-number restriction and traceability.

## Design principles

### Location is a tree; holding is a role, not a type

Areas are fixed structure; everything else owned is an item; and any non-fungible item can hold other items — either as a **container** (storage) or an **assembly** (composition), named by its `DurableKind`. Collapsing "holder" into "a role an item plays" rather than a separate entity means one ID space, uniform handling of mixed contents (a bin holds both a tracked camera and a pile of screws), and no special table for "storage." Splitting the role into two kinds keeps the tree honest: a place you browse into and a machine whose parts ride along are genuinely different, and conflating them (the old `IsContainer` boolean) wrongly blocked selling a machine as a unit. It is the single decision that makes the rest of the model fall out.

### Resolved location is derived, never stored

An item's area is always computed by walking the containment chain, never cached on the row. This is load-bearing: it is what makes moving a packed container a one-row change instead of a cascade, and it is the only way to *guarantee* the resolved location can never drift from the truth. The cost — a walk up a short parent chain — is negligible at household scale, and the temptation to denormalise is explicitly rejected.

### Fungibility splits how quantity works, and nothing else

The only reason the two kinds exist is that a single thing and a bulk consumable need genuinely different handling: one location vs many, identity vs count, "where is it" vs "how much is left." Everything that *can* be uniform across the two is — they share one table, one ID space, status, value, audit, and containment. The discriminator is neutral and persisted; the user-facing labels are not.

### Status is the state of the thing, not soft-delete

Homestead's cross-cutting stance is no soft-delete and no undo outside Documents' trash. Inventory honours that by treating status as a *fact about the physical object* — you sold the drill; you threw out the dead batteries — rather than a deletion flag on the record. Terminal items are filtered from default views the way archived Finances accounts are (a reveal toggle), and a true delete remains available for mistakes, gated on whether the row carries history worth keeping. This keeps Inventory inside the existing principle instead of carving a second soft-delete exception.

### Current state plus replayable history

A fungible item's per-location quantity is kept as live state (the stock entry) *and* every change to it is appended to the movement ledger in the same transaction. The live value is always reproducible by replaying the ledger — the two never disagree, because one application service writes both or neither. This is what lets v1 surface fast current-state reads now while keeping the door open to derived-on-hand and consumption analytics later, with no UI change, because every mutation already funnels through one place.

### Capture history from day one

Consumption over time and per-purchase cost can only be known if they are recorded *as they happen* — there is no backfill that invents a history the household never captured. So the movement ledger and optional unit cost are written from v1 even though v1's screens mostly show current state. This mirrors Finances' "accumulate from day one" stance: the tracker reflects only what was actually captured, so it must capture from the start.

### Every amount carries its currency

Following Finances, no monetary amount is ever stored bare. An item's estimated value, its acquisition cost, and a movement's unit cost are each paired with a currency (a reference to the seeded, read-only currency set), recorded both-or-neither. v1 shows each amount in the currency it was entered in; converting or rolling values into a household total is deliberately left to the future Finances integration, so nothing is silently re-denominated and no painful currency backfill is ever needed.

### Inventory data is household-global

Like Documents and Finances, Inventory has no per-item owner and no per-user scoping in v1. `CreatedByUserId`-style attribution is recorded for audit and display, never for access control. Writes require a contributor role; everything else a signed-in household member can see.

## Distinctive UX decisions worth preserving

1. **Two browse lenses, not one — over one list.** Durables and consumables share a single unified list (the "durables vs consumables" split is a one-tap kind filter, not separate pages), and everything in it is reachable both **by place** (the list's area filter, plus **Browse** for walking into areas and containers — consumables included, since each stock entry resolves to an area or container) and **by type** (the Consumables kind filter shows every fungible type with its total across all locations, low-stock surfaced). Consumables never require remembering which room they live in.
2. **Unplaced is a real place.** An item with no location is not an error or a hidden orphan — it shows in its own Unplaced bucket, so "add it now, shelve it later" is a supported flow and nothing falls through the cracks.
3. **Moving a container moves its contents, visibly and for free.** The user changes one thing — where the box is — and everything inside follows, because location is derived. There is no "also update the 30 things inside" step.
4. **Consumables show total *and* breakdown.** A fungible item surfaces its total on-hand prominently with a per-location breakdown beneath, and per-location quantity controls in context — so "how much do I have" and "where is it" are both answerable without leaving the screen.
5. **Low-stock is surfaced, not buried.** Items at or below their restock threshold are flagged inline in the unified list rather than requiring the user to go looking, and a one-tap "Low stock only" filter turns the list into a de-facto reorder view.
6. **"Track this one individually" is one action, not a conversion ceremony.** Pulling a unit out to track it on its own is a single, lossless, atomic action that feels like flipping a switch — the user never sees stock and item tables being reconciled.
7. **Terminal items leave quietly.** Selling, discarding, or archiving an item removes it from the default picture without deleting its record, and a single reveal toggle brings the inactive items back when the user wants the full history.
8. **Triage rides along with spatial navigation, as a collapsible warning region inside Browse.** `/inventory` redirects to **Browse** (`/inventory/browse`) — there is no separate Overview surface. Browse's root is a card grid — one card per area (name + resolved count + a per-area low-stock badge when nonzero; no rename/delete, those stay on the Areas page) plus an Unplaced card only when its count is nonzero. (Item search is not a per-page box on Browse — it lives in the shell-wide command palette / ⌘K; see [universal-search](../shared/universal-search.md).) Above the grid sits a **conditional, collapsible "needs attention" region**, hidden entirely when there's nothing to report so a healthy household lands on a clean Browse. **At the root** that region is **global** (household-wide), reusing the same overview data the grid already fetches: three tiles — low-stock consumables (the de-facto shopping list, previewed most-urgent-first by on-hand-to-threshold ratio), lent durables *with the to-whom*, and unplaced durables (resolved semantics, so something buried in an unplaced container counts) — each showing an exact count, a short preview, and a "View all" landing on the unified list pre-filtered to that lens. A tile with nothing to report says so ("Nothing lent out") instead of rendering an empty list; the whole region only renders when at least one of the three is nonzero. **When drilled into an area** the region re-scopes to that place (**resolved semantics**: "low consumables stored here" — globally-low consumables whose stock resolves into this place, *not* a per-location threshold — plus "durables lent from here"), as **count-only** panels (the actual items already appear in the place's contents list right below) with no Unplaced panel (it's the absence of a place). A drilled **container** scopes the same panels by its *direct* contents and renders them **link-free** — the list filters by area, not by container, and the contents already sit in the table right below; an **area** keeps a "View all" `?area=` link. It defaults **collapsed in every scope** (root and drilled alike), so the panels never fight the page for vertical space; the open/closed toggle is re-seeded only on root↔place transitions, so drilling area→area preserves a manual toggle. Tile and panel "View all" deep links seed the list's filters via **one-shot URL params**, read once and then stripped (full two-way filter↔URL sync is a deliberate later step — the status-lens chips); that one-shot-param stance governs *only* these list deep links. Clicking an area or container card **drills into that place on its own route**: child-**container cards** that drill deeper (subtree count, low-stock badge, a ≤3-name contents preview), and an inline list of the place's direct durables and consumables — consumable rows showing the **local** quantity with an "of N total" hint. Rows open the existing **quick panels in place** (the drilled container's own record is reachable via a header **"View details"** button that routes to its detail page), and a **"New item"** button opens the create dialog with the location pre-filled. Counts use resolved semantics on the place cards (durables resolving to the place incl. nested-in-containers, plus distinct consumables with stock resolving there) — that's the count shown *before* you drill in, to help decide where to look. The drilled header itself carries **no count subtitle**, just the place name: the child cards and contents list directly below already enumerate what's here, so a header tally would be redundant. (An earlier "N here · M more inside containers" split was dropped — it conflated sub-area and container cards with items under a vague "here", and because it counted sub-area *cards* while the card roll-up counts their *contents*, it never cleanly reconciled with the clicked card.) The drill state lives in the **route itself** — one route per drilled node (`browse/area/:id` | `browse/item/:id` | `browse/unplaced`), the URL naming only the *current* node — with the ancestor trail rendered as a **header breadcrumb** (All places › area › container…, each segment's target derived from its kind, since container nesting is unbounded). Back/forward, refresh, and bookmarks restore the exact node for free, because every level is a real URL. Folding triage into Browse is deliberate: the always-render-empty triage dashboard felt too sparse as a standalone page, and a household walking the house wants the "what needs attention here" callouts in the same place — surfaced when there's something to report, tucked away when there isn't. The sidebar lists **Browse** as the single top-level surface, with **Items** and **Areas** under a "Manage" group — Areas is the admin/CRUD surface whose name cells link into the area-filtered list, and its table count uses the same resolved semantics, so table, card, and list agree.
9. **Day-to-day changes happen in a quick panel, not a page.** Clicking a row in the unified items list opens the kind-matching side panel over the list (the durable panel or the consumable panel) — steppers for stock, quick move, quick status change (marking something Lent collects *to whom* inline before the status applies) — so routine updates never cost a navigation. Two rules shape the panel: **nothing stacks on it** (every sub-interaction is an inline expansion, never a second sheet), and **full editing is deliberately excluded** (edit stays on the row actions and the detail page, keeping the panel a fast read-and-adjust surface). Both panels share **one anatomy** (the same pass the detail pages got): a **header** of identity + status badge (shown *always* for a durable, whose status is rich — Active/Lent/InRepair/Sold/Discarded/Archived — but *only when archived* for a consumable, whose status is effectively binary and whose always-on "Active" chip would be noise on a fast surface), a **kind-specific body** carrying every fast action (durable: a Location card and a mirroring Status card, each with its inline form — Move under Location, Lent-to under Status — plus container contents; consumable: the on-hand summary with its low-stock warning, per-location steppers, and assign/move/correct), and a **footer** meta strip — `Updated {age}` on the left, `View details →` on the right (the consumable's link first flushes any pending optimistic stepper taps, then navigates; the durable's is a plain link, as its patches apply synchronously). The durable's Details card is hidden when it carries no serial/value/acquisition (kind, date, cost, or acquired-from)/lent/notes. **Both kinds have a detail page** at `/inventory/items/:id` sharing one anatomy: an `Items › name` breadcrumb, a title with its status badge, a prominent **headline answer** (resolved location for a durable, total on-hand for a consumable), `Edit` plus a `⋮` overflow menu carrying **Archive/Restore + Delete**, a kind-specific body (durable: details + contents; consumable: per-location breakdown + movement ledger), and **History** last. The panel stays the fast read-and-adjust surface while full editing and lifecycle (archive/delete) live on the detail page and the row actions. An archived consumable's panel is read-only.
10. **Tags classify across the grain of place — hierarchically, with descendant-expanding filters and inline creation.** Tags are the classification axis orthogonal to Area (where) and Brand (who made it): a multi-valued, path-delimited tree applied to both durables and consumables. The filter facet is not a flat multi-select — selecting `Electronics` with the **"Include sub-tags"** toggle pulls in everything under it (`Electronics/Batteries`, `Electronics/Batteries/AA`, …) by path prefix, so a broad tag answers a broad question without enumerating leaves. Tags are applied without leaving the item form: a path-based **find-or-create** chip input autocompletes existing tags and creates new paths (upserting ancestors) inline, so classifying a new item never requires a detour to a separate tag-admin step — while a dedicated `/inventory/tags` page still owns deliberate rename/recolour/re-parent/delete. Parent colours cascade to uncoloured descendants, and a badge click on a quick panel, detail page, or the tag table deep-links to the tag-filtered list.

## Future-compatibility

The following must be addable later without re-migrating items, areas, stock, or movement history:

- **A richer Brand surface.** v1 ships brand as a find-or-create lookup with a filter facet and detail chip, plus a Brands management page for rename / delete / merge (with per-brand item counts). Deferred, all additive on top of the existing `Brand` row and `Item.BrandId` FK: **brand metadata** (logo, website, support contact) as extra columns on the brand row; brand as an **Items-list column**; a **per-brand value rollup**; and making brand **searchable** (a trigram index + search-query wiring) so it joins the ⌘K palette rather than staying facet-only.
- **Photos / attachments** on items, in their own table keyed by `Item.Id` — important for non-fungible items and insurance claims.
- **Barcode / QR scanning** and **printable container labels**, keyed off the stable item and area IDs.
- **Expiry / perishables and per-category custom fields**, feeding reminders through a future Calendar/Maintenance module.
- **Per-location restock thresholds**, replacing or supplementing the single global threshold — the threshold is already an item-level value compared against the total, so a per-location variant is an additive column, not a reshape.
- **Value rollup and display-currency conversion**, via the Finances integration — values already carry their currency, so aggregation is a read-time concern with no data change.
- **Cost and consumption reporting** built on the movement ledger, which is captured from day one for exactly this reason.
- **A move from kept-current-quantity to derived-on-hand**, computing stock from the ledger — possible without UI change because all quantity mutations already funnel through one service.
- **Cross-module links** — receipts and warranties (Documents), value rollup (Finances), consumables → maintenance schedules (a future module). `Item.Id` is a stable FK target so these attach without reshaping Inventory.
- **Offline / sync.** The uuid PKs, delta-based ledger, and lifecycle-status-instead-of-hard-delete keep this cheap to approach; the hard parts (merge/conflict resolution, client-side ID generation) are deferred, with only the `uuid` PK *type* locked now since changing a PK type after FKs exist is the one painful migration.
- **Multi-user / per-item permissions** if the household ever expands beyond a single trust boundary.

None of these should require re-migrating historical inventory data.
