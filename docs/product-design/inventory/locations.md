# Locations & Containment — Reference

Detailed design for areas, the containment tree, resolved location, and the non-fungible item lifecycle in the Inventory module. Expands on the summaries in [overview.md](overview.md#area). Read this before designing, implementing, or reviewing anything touching where items live, how containers nest and move, or how items are archived and deleted.

The fungible side — stock entries, the movement ledger, thresholds, and the conversion operations — lives in [stock.md](stock.md); this document treats fungible stock only where it intersects containment (stock sitting inside a container) and lifecycle (what stock blocks a delete).

## Conceptual model

Three structural facts drive everything here:

1. An **Area** is fixed structure — a place that exists independent of what is in it. Areas nest into a hierarchy (`House › Basement › Bedroom`).
2. An **Item** is anything owned. A non-fungible item lives in exactly one location, or nowhere.
3. A **location** for a non-fungible item is *either* an area *or* another item acting as a **parent** — never both, and optionally neither. Any live non-fungible item can be a parent: an `IsContainer = true` item holds *stored-in contents*; an `IsContainer = false` item holds *components*.

Every non-fungible item carries a boolean **`IsContainer`**: `true` for a storage *place* you browse into and file loose things under, `false` for a regular item. A child's relationship is **derived from whether its parent is a container**: a child of a container is *stored in* it; a child of a non-container is a *component of* it (making the parent an "assembly"). There is no per-child attachment flag, and assembly-ness is not stored — it is derived from a non-container having component children.

Because a container is just a non-fungible item with `IsContainer = true`, "what's in the garage" and "what's in this bin" are the same query at different depths, and an item's true area is found by walking up until structure (an area) is reached — through *any* non-fungible parent, container or not, so a component resolves its area through its (non-container) parent. Area nesting applies that same upward walk a second time — to the area's own parent chain — so resolution climbs parents to a leaf area, then climbs that area's ancestors to the root.

## Areas

An area is a named place that optionally nests inside another area. In v1 it carries:

| Field | Notes |
|---|---|
| `Id` | uuid |
| `Name` | "Garage", "Home Office", "Pantry" |
| `ParentAreaId` | nullable self-reference; `null` = a top-level (root) area. Mirrors `ParentItemId` on items |
| `CreatedAt`, `UpdatedAt` | UTC |

Areas form a **hierarchy** via the optional `ParentAreaId` (`House › Basement › Bedroom`). Nesting is additive over the flat model: an existing flat area is simply a root (null parent), so there is no backfill. There is intentionally no `Kind` (Room/Area/Building) field: nothing in v1 branches on it, so it is omitted rather than carried unused.

**Placement is any-level**: an area can hold sub-areas *and* its own direct items/containers at the same time — there is no leaf-only restriction (an area behaves like a container in this respect). **Nesting depth is unbounded** (mirroring "containers nest to any depth"); the only structural guard is cycle prevention.

**Resolution & roll-up.** `ResolvedAreaId` stays the **leaf** area an item/stock directly sits in (e.g. Bedroom) — unchanged by nesting. What changes is that the displayed **path** gains the full area-ancestor chain above the leaf (`House › Basement › Bedroom › Chest › Bin`), and **counts roll up**: every ancestor area's count includes its descendant areas' contents (a drill in Bedroom counts toward Bedroom, Basement, *and* House). A consumable type stocked in two sub-areas counts **once** for a shared ancestor (set-union dedup), so the rolled card count still equals what the area-filtered list shows. This is computed at read time by two walks that must stay in lock-step — `ItemLocationResolver` (per-item path) and `InventoryPlacementService` (aggregate counts).

**Creating** an area needs a name and an optional parent. **Editing** changes the name and/or re-parents the area (including promoting it to a root by clearing the parent); re-parenting moves the whole subtree with it automatically, since resolution is derived and nothing cascades. **Deleting** is governed by what currently points at it — see *Area lifecycle* below.

## The item record

A single `Item` table holds both kinds, discriminated by `Fungibility`. Shared fields apply to every item; the kind-specific columns are null on the other kind (and the kind-specific check constraints enforce that).

| Field | Applies to | Notes |
|---|---|---|
| `Id` | all | uuid |
| `Name` | all | |
| `Fungibility` | all | `Fungible` \| `NonFungible` — **immutable** for the life of the row |
| `Status` | all | lifecycle (see below); fungible items use `Active`/`Archived` only |
| `Notes` | all | nullable |
| `CreatedAt`, `UpdatedAt` | all | UTC |
| `CreatedByUserId`, `UpdatedByUserId` | all | attribution; nullable, cleared if the user is deleted |
| `BrandId` | all | optional FK to the canonical `Brand` lookup; single-valued; applies to **both** kinds; `OnDelete(Restrict)` — a brand in use can't be deleted. See [overview.md](overview.md#brand) |
| `AreaId` / `ParentItemId` | non-fungible | the location pair — **at most one** set (see *The location rule*) |
| `IsContainer` | non-fungible | `true` = a storage container; `false` = a regular item. Set on every non-fungible item (default `false`); **null on fungible**. Any non-fungible item may be a parent |
| `Compartment` | non-fungible | free-text sub-position within a parent ("top drawer"); also doubles as the component slot label under a non-container ("CPU"); nullable |
| `Value` / `ValueCurrencyId` | non-fungible | estimated value + its currency — both or neither; nullable |
| `Serial` | non-fungible | nullable |
| `Model` | non-fungible | free-text model designation ("MRW23"); sits beside `Serial`; nullable |
| `AcquiredOn` | non-fungible | date; nullable |
| `AcquisitionKind` | non-fungible | how the item entered the household: `Purchased` \| `Gift` \| `Inherited` \| `Other` (string-backed); null = not recorded — the normal state for a backfilled household |
| `AcquisitionCost` / `AcquisitionCostCurrencyId` | non-fungible | purchase price + its currency — both or neither; a historical fact, distinct from the estimated `Value`; nullable |
| `AcquiredFrom` | non-fungible | free-text source — a store or a person ("Home Depot", "Mom"); `LentTo` precedent, no vendor entity; nullable |
| `LentTo` | non-fungible | free text; set when `Status = Lent` |
| `Unit` | fungible | "screws", "rolls", "gal" — see [stock.md](stock.md) |
| `RestockThreshold` | fungible | optional global low-stock trigger — see [stock.md](stock.md#total-on-hand-and-low-stock) |

`Value` references the seeded, read-only currency set (the same commodities Finances uses for currencies); v1 shows it in the entered currency and does not convert or roll it up — see [overview.md](overview.md#every-amount-carries-its-currency). Fungible-only fields and their behaviour are detailed in [stock.md](stock.md); this document owns the non-fungible side.

## The location rule

A non-fungible item's location is held as a **pair of optional foreign keys** — one to an area, one to a parent item — with **at most one set**:

- `AreaId` set, `ParentItemId` null → the item sits directly in an area.
- `ParentItemId` set, `AreaId` null → the item sits inside a parent (stored in a container, or a component of a non-container — derived from whether the parent is a container).
- both null → the item is **Unplaced**.
- both set → **disallowed**.

This is enforced with a check constraint in the house style (an explicit "not both set" condition, matching how Finances writes its paired-nullable constraints — not a `num_nonnulls` helper). The FK-pair shape is chosen deliberately over a polymorphic string or a location supertype, because the set of things an item can be "in" is permanently closed at exactly two — an area or a container item. (A shared `Location` supertype would only earn its place if locations later gained shared attributes — a QR code, a capacity, coordinates, a photo. They do not in v1.)

> **Why location is optional (at most one), not mandatory (exactly one).** Requiring every item to be somewhere forces a fake location at creation time and has no honest answer for "I own it but haven't shelved it," "it's in transit," or "I sold it and don't recall where it last sat." Unplaced is a legitimate state, so the constraint is *at most one*, and "Unplaced" is surfaced as a real bucket rather than hidden.

**Stock entries are different.** A unit of fungible stock requires *exactly one* of area / container — a stock entry with no location is not "stock somewhere," it is just a number, which is what the total on-hand already is. That asymmetry is intentional; see [stock.md](stock.md#the-stock-location-rule).

## Containment vs composition

The same `ParentItemId` edge models two different relationships, distinguished by the **parent's `IsContainer`**:

- **Storage containment** — a child of a **container** (`IsContainer = true`) *sits in* it. The container is a *place* you browse into and file loose things under. Its contents are non-fungible items whose `ParentItemId` is this container, plus fungible stock whose stock entry's container is this container (rendered at that entry's local quantity).
- **Composition** — a child of a **non-container** (`IsContainer = false`) is a *component of* it (a CPU in a PC): installed, not stored. A non-container is **not a place** — it does not appear in spatial Browse drill-cards and cannot take loose stock — but its components are first-class items shown under the parent's detail "Components" section. A non-container with ≥1 live component child is an **assembly** — a *derived* status (`HasComponents`), never stored.

`Compartment` is reused as the **component slot label** ("CPU", "RAM slot 1") for a child of a non-container, exactly as it records sub-position ("top drawer") inside a container. Structured/empty slots are deferred (a future `ItemSlot` table; out of scope).

**Typing invariants** (application-enforced — they span rows and cannot be plain check constraints):

- A `ParentItemId` must reference a **live non-fungible** item. Any non-fungible item can be a parent — a container holds contents, a regular item holds components — but a fungible consumable can never be one.
- A `StockEntry.ContainerItemId` must reference a non-fungible item with `IsContainer = true` — loose stock can only sit in a storage place, never in a non-container.
- `IsContainer` is set on **non-fungible items only** — it is null for a fungible consumable (enforced by check constraint: fungible ⇒ `IsContainer IS NULL`, non-fungible ⇒ `IsContainer IS NOT NULL`).
- `IsContainer` **cannot be flipped** while the item still holds any child item (live *or* terminal) or any stock entry pointing at it — in *either* direction. This single guard replaces the trichotomy's web of transition rules: stored contents lock a container as a container (flipping it would reinterpret them as components), and components lock a regular item as a non-container (flipping it would reinterpret them as stored stock). Empty it first. (A brand-new item holds nothing, so flipping it is always free — how you pre-declare an empty bin as a container.)

**Nesting and cycles.** Parents nest to any depth (bin in cupboard in garage; a component could itself be a container or an assembly). Because containment is a parent chain spanning *both* relationship kinds, a move must not create a cycle: an item cannot be placed inside itself or inside any of its own descendants. Moving an item therefore validates that the proposed new parent is not the item itself or anywhere in its subtree, by walking up from the target across every non-fungible parent. This is the one structural guard on moves.

## Resolved location

An item's **resolved area** is derived, never stored:

```
resolveArea(item):
  while item.ParentItemId is set:
    item = parent(item)
  return item.AreaId        # may be null → Unplaced
```

Walk up the parent chain to the first item that sits directly in an area; that area is the answer. The walk crosses **any** non-fungible parent, container or not, so a component resolves its area *through* its (non-container) parent (`Garage › Gaming PC` ⇒ the CPU resolves to the Garage). If the chain ends at an item that is itself Unplaced (no area, no parent), the whole chain resolves to **Unplaced**. The resolved location may be presented as a path for context (`Garage › Workbench cupboard › Small-parts bin`), but only the leaf item's own one-step location is ever stored.

Resolved-Unplaced is the semantics every Unplaced surface uses — the area filter's Unplaced bucket and the overview landing page's Unplaced tile both count an item nested inside an unplaced container, not just items with no direct location — so a tile count and the list it deep-links to always agree.

**Components count toward their resolved area, but are not in the area's Browse drill-in.** Because a component resolves through its (non-container) parent to an area, it **counts** toward that area's resolved place-card count and appears in the area-filtered Items list (card count == area-filtered list, consistent by design). It is *not* listed in the area's Browse drill-in contents — its parent is the assembly, not the area — and the assembly itself renders as a plain item row in the area, never a drillable place card. The assembly's components are reached through its detail page's Components section instead.

### Rendering a resolved path

A resolved path is rendered as **links** — one per segment — wherever the path is the *subject* of the line: the durable detail headline, the durable quick-panel's Location card, and the consumable detail page's "By location" table. Each segment's target derives from its **kind**, never its position:

| Segment kind | Target |
|---|---|
| `Area` | the area's Browse node |
| `Container` | the container's Browse node |
| `Item` | the item's **detail page** — a non-container is not a place (see [Containment vs composition](#containment-vs-composition)) |

`Unplaced` — the synthetic root prefixed onto a chain that resolves to no area, and the whole path for a directly-unplaced item — links to the Unplaced bucket and renders muted. A `Part of {assembly}` line links only the assembly. Segment text is unchanged from the plain-text rendering (`Garage › Cupboard › Bin`); the links are additive.

These are **inline links, not a breadcrumb landmark**: only the Browse header renders a `<nav>` breadcrumb, so a page never announces two.

Three surfaces deliberately stay **plain text**, and should not be "fixed for consistency" later:

- **The Items list Location column** — the row itself is the click target (it opens the quick panel, which *does* carry links), so links there would mean swallowing the row click plus 2–4 extra tab stops *per row*. The cell also truncates from the end, cutting the leaf — the segment worth clicking — first. The cost of skipping is one extra click.
- **The consumable quick-panel's per-location rows** — same pathology in a ~448px sheet, where the label shares one line with a quantity stepper and two actions. It is also the only surface with pending optimistic stepper writes that are deliberately flushed before navigating away. The panel footer's "View details" already reaches the detail table, where the links live.
- **Browse's "Recently updated" chips** — each row is a `<button>`, and an `<a>` inside a `<button>` is invalid HTML.

Because a path rendered as links and a path rendered as text must never disagree, both come from one view model; the plain-text form is that view model flattened, not a parallel implementation.

**Never denormalise this.** Caching a resolved `AreaId` onto nested rows is explicitly disallowed — it is precisely the optimisation that reintroduces move-with-contents drift. The walk is short and cheap at household scale; the codebase already resolves hierarchies in application code rather than via stored rollups or recursive SQL, and Inventory follows that idiom. The *mechanism* (in-memory walk, a recursive query, or a mapped view) is an implementation choice left open; the **invariant — resolved location is derived, never stored — is locked.**

## Move-with-contents

Moving an item changes **only that item's own location pair** — its `AreaId`/`ParentItemId`. Everything inside it follows automatically, because every descendant's and every contained stock entry's resolved area is computed through the chain that now ends at the moved item's new spot. There is no cascade write and no stored value to reconcile.

Concretely, the supported moves are:

- **Into an area** — set `AreaId`, clear `ParentItemId`.
- **Into a parent** — set `ParentItemId` (to any live non-fungible item, cycle-checked), clear `AreaId`. An optional `Compartment` free-text note records sub-position within the parent ("top drawer" in a container, "CPU" in a non-container assembly); it is descriptive only and anchors nothing.
- **Out to Unplaced** — clear both.

A fungible consumable is never "moved" as a unit — its stock is transferred between locations entry-by-entry; see [stock.md](stock.md#transfers).

## Non-fungible item lifecycle

An item's `Status` records the state of the physical object. Non-fungible items use the full set; fungible consumables are restricted to `Active`/`Archived` (see [stock.md](stock.md#fungible-status)).

| Status | Meaning | In default views? | Terminal? |
|---|---|---|---|
| `Active` | Owned, in use or storage | Yes | No |
| `Lent` | Lent out (with free-text `LentTo`) | Yes | No |
| `InRepair` | Out for service | Yes | No |
| `Sold` | Sold off | No (reveal toggle) | Yes |
| `Discarded` | Thrown out | No (reveal toggle) | Yes |
| `Archived` | Kept in records, not active | No (reveal toggle) | Yes |

**Lent** additionally carries a free-text `LentTo` ("Dave next door") — there is no contacts graph in v1. Lending is a status, not a move: a lent item keeps its location (you still know where it normally lives), and lending a *container* does not cascade any status onto its contents — the contents' effective state is derived at read time, not written. (Pack a bag, lend the bag; the items inside are "with the bag," not individually re-statused.)

**Terminal statuses keep their last location by default.** A `Sold` or `Discarded` item retains the `AreaId`/`ParentItemId` it had, so "where did I last keep the camera I sold?" stays answerable. This is why location is *at most one*, not *exactly one* — a terminal item is free to keep a real location or to have been Unplaced; both are valid. The location is *kept*, not *frozen*: a terminal item's location remains editable afterwards (see the escape hatch below), so a leftover record can still be moved out of a container you want to empty.

**Hidden-by-default, revealed by a toggle.** Terminal items drop out of default lists and out of a container's default contents view, and a single "Show inactive (N)" filter brings them back — the same affordance Finances uses for archived accounts, not a global query filter. (A global hidden-row filter would also be wrong here for a concrete technical reason: it would sever the parent chain when resolving an item that sits inside an archived or sold container, breaking the walk.)

### Holder emptiness and terminal status

How a parent's terminal transition is gated depends on **whether it is a container**, because storage and composition strand contents differently:

- **A container taken terminal** (`Sold`/`Discarded`/`Archived`) is blocked **only by *live* contents** — `Active`/`Lent`/`InRepair` stored-in children and any stock entries. Terminal children (an already-sold thing that was in the box) do **not** block it: they are hidden from the default contents view and so cannot strand the parent, and the item **stays a container** (`IsContainer` unchanged) when it goes terminal, preserving the parent-is-a-live-place invariant for those leftovers.
- **A non-container (an assembly) taken terminal is *never* blocked by its components.** Selling a PC as a unit sells it with its CPU and RAM inside — the components **ride along**: their effective state is derived at read time (like a lent container's contents), with **no cascade write** onto the component rows. This is exactly the behaviour the storage-vs-composition line exists to protect.
- **Flipping `IsContainer`** (either direction) is the stricter, symmetric guard: it is blocked while **any** child item — live *or* terminal — still points at the item, or any stock sits in it. A flip would reinterpret existing children (contents ↔ components), and a leftover record's `ParentItemId` would then mean the wrong thing. Empty it first. (A brand-new item holds nothing, so flipping it is always free.)

The asymmetry is deliberate. A terminal container is still a place (its `IsContainer` is intact), so a terminal leftover stored in it remains coherent. A non-container's components are *part of* the unit, so they follow it wholesale rather than blocking it. Flipping `IsContainer` reinterprets what the children are, so nothing may still be pointing in.

**The complement — nothing moves *into* a terminal holder.** The sealing gate closes one direction (a live→terminal transition is blocked by live contents); the destination side closes the other. A receive/transfer/convert-in **destination**, and a container chosen as an item's **parent** (on create, re-parent, or track-one), must be a **live** (`Active`/`Lent`/`InRepair`) container. Things move **out of** a terminal (`Sold`/`Discarded`/`Archived`) container — the escape hatch above — but never **into** one. Sources stay existence-only. An *adjust* that **lowers** the count is a source too, so already-stranded stock can still be reconciled **out**; but an *adjust* that **raises** it is an inflow and is rejected into a terminal container like any other destination.

**The escape hatch — terminal records stay movable.** Keeping the flip rule strict would otherwise dead-end: a container holding only sold/discarded leftovers could never be flipped back to a regular item, because those leftovers block it and a terminal item's location looks frozen. It is not frozen. The location lock applies **only to the live→terminal transition patch itself** (so a "sell and move" in one step keeps the item where it was); an **already-terminal** item's `AreaId`/`ParentItemId`/`Compartment` are freely editable afterwards. So the path to emptying such a container is: move each leftover record out (to an area or to Unplaced), then flip `IsContainer`.

*Future work:* a one-click "release N inactive records" action could unplace all terminal children of a parent in a single step, so a user does not have to move each leftover by hand before flipping `IsContainer`. Not built in this slice — the per-record escape hatch above is the supported path today.

### Delete vs archive

Inventory keeps the Finances delete-vs-archive split:

- **Archive** (`Status = Archived`) is the keep-the-record path — the item leaves the active picture but its row, history, and any attachments survive. This is the normal way to retire something you no longer want listed.
- **Delete** is the mistake-eraser, and is **disabled once the item carries history worth keeping**: a non-fungible holder that still has any child (stored-in contents *or* components) or stock, or a fungible item with movement-ledger rows, cannot be hard-deleted — it can only be archived (the `ParentItemId` FK is `Restrict`, the backstop behind the handler check). A freshly mis-created item with no history can be deleted outright.

Both **Archive/Restore** and **Delete** are surfaced on the item detail page's `⋮` overflow menu for **both** kinds (durable and consumable alike), alongside the unified list's row actions; Delete stays gated exactly as described above (the blocked reason — "has contents / stock history — archive instead" — is shown inline in the confirm dialog).

This mirrors `accounts.md`'s rule that Delete is present but disabled once an account has transactions, and the "Show archived (N)" reveal in the same module.

## Area lifecycle

Deleting an area is governed by what references it:

- **Blocked while it has sub-areas** — a parent can't be removed out from under its children. The handler raises a friendly "Area has sub-areas and cannot be deleted. Move or delete them first." ahead of the FK (`ParentAreaId` is `Restrict`, the same backstop the container chain uses). Checked first — it's the cheapest guard. Re-parent or delete the children first.
- **Blocked while anything live resolves to it** — the same resolved count the areas table and the overview's place cards show (live items resolving there directly or via container chains, plus distinct consumables with live stock resolving there), so the UI's delete gating and the backend rule can't disagree. Additionally blocked by **any stock entry directly in the area regardless of consumable status** — an archived consumable's stock is hidden from counts and lists but still sits there (a stock entry requires exactly one location), so it must be moved or used up first. Move or retire the rest first.
- **Historical movement references survive a deletion.** The movement ledger records the area an inflow/outflow/transfer happened at; those references are cleared (set null) rather than blocking the delete, so the quantity history and what happened to it persist even though the now-deleted area's label is gone.

**Re-parenting** is a metadata edit (PATCH `/parentAreaId`), not a move of contents: because an item's area is *derived* by walking up, moving an area to a new parent automatically takes its whole subtree with it — nothing cascades. The only rule is a **cycle guard**: an area cannot be set as its own parent, nor moved underneath any of its own descendants. The walk climbs from the proposed parent up its `ParentAreaId` chain and rejects (`BusinessRuleException`) if it reaches the area being moved — exactly mirroring the container cycle check on `ParentItemId`. Setting the parent to `null` promotes the area to a root.

There is no area archive in v1 (areas carry no lifecycle status); an area is either present or deleted. If a household stops using a room, the practical path is to empty it and delete it, accepting that historical movements that happened there lose their location label. (An area archive/rename-only path is a natural future addition if losing those labels proves annoying.)

## Audit

Every mutation here writes to the shared field-level audit log via the standard logger — area create/rename/delete, item create/edit/move/status-change, `IsContainer` (type) changes. Inventory contributes per-entity snapshots (Area, Item) so the diffs are captured, consistent with every other module. This is separate from, and complementary to, the fungible movement ledger in [stock.md](stock.md): the audit log records *metadata* edits (a rename, a threshold change, a move); the movement ledger records *quantity* history. Neither replaces the other.
