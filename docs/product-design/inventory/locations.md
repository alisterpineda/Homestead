# Locations & Containment — Reference

Detailed design for areas, the containment tree, resolved location, and the non-fungible item lifecycle in the Inventory module. Expands on the summaries in [overview.md](overview.md#area). Read this before designing, implementing, or reviewing anything touching where items live, how containers nest and move, or how items are archived and deleted.

The fungible side — stock entries, the movement ledger, thresholds, and the conversion operations — lives in [stock.md](stock.md); this document treats fungible stock only where it intersects containment (stock sitting inside a container) and lifecycle (what stock blocks a delete).

## Conceptual model

Three structural facts drive everything here:

1. An **Area** is fixed structure — a place that exists independent of what is in it.
2. An **Item** is anything owned. A non-fungible item lives in exactly one location, or nowhere.
3. A **location** for a non-fungible item is *either* an area *or* another item acting as a container — never both, and now optionally neither.

Because a container is just a non-fungible item with its container role enabled, "what's in the garage" and "what's in this bin" are the same query at different depths, and an item's true area is found by walking up until structure (an area) is reached.

## Areas

An area is a flat, named place. In v1 it carries only:

| Field | Notes |
|---|---|
| `Id` | uuid |
| `Name` | "Garage", "Home Office", "Pantry" |
| `CreatedAt`, `UpdatedAt` | UTC |

Areas are **flat** — no parent area, no nesting — in v1. (Nested areas are a named future addition; see [overview.md](overview.md#future-compatibility).) There is intentionally no `Kind` (Room/Area/Building) field: nothing in v1 branches on it, so it is omitted rather than carried unused.

**Creating** an area needs only a name. **Editing** changes the name. **Deleting** is governed by what currently points at it — see *Area lifecycle* below.

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
| `AreaId` / `ParentItemId` | non-fungible | the location pair — **at most one** set (see *The location rule*) |
| `IsContainer` | non-fungible | container role; only a non-fungible item may be a container |
| `Compartment` | non-fungible | free-text sub-position within a parent ("top drawer"); nullable |
| `Value` / `ValueCurrencyId` | non-fungible | estimated value + its currency — both or neither; nullable |
| `Serial` | non-fungible | nullable |
| `AcquiredOn` | non-fungible | date; nullable |
| `LentTo` | non-fungible | free text; set when `Status = Lent` |
| `Unit` | fungible | "screws", "rolls", "gal" — see [stock.md](stock.md) |
| `RestockThreshold` | fungible | optional global low-stock trigger — see [stock.md](stock.md#total-on-hand-and-low-stock) |

`Value` references the seeded, read-only currency set (the same commodities Finances uses for currencies); v1 shows it in the entered currency and does not convert or roll it up — see [overview.md](overview.md#every-amount-carries-its-currency). Fungible-only fields and their behaviour are detailed in [stock.md](stock.md); this document owns the non-fungible side.

## The location rule

A non-fungible item's location is held as a **pair of optional foreign keys** — one to an area, one to a parent item — with **at most one set**:

- `AreaId` set, `ParentItemId` null → the item sits directly in an area.
- `ParentItemId` set, `AreaId` null → the item sits inside a container.
- both null → the item is **Unplaced**.
- both set → **disallowed**.

This is enforced with a check constraint in the house style (an explicit "not both set" condition, matching how Finances writes its paired-nullable constraints — not a `num_nonnulls` helper). The FK-pair shape is chosen deliberately over a polymorphic string or a location supertype, because the set of things an item can be "in" is permanently closed at exactly two — an area or a container item. (A shared `Location` supertype would only earn its place if locations later gained shared attributes — a QR code, a capacity, coordinates, a photo. They do not in v1.)

> **Why location is optional (at most one), not mandatory (exactly one).** Requiring every item to be somewhere forces a fake location at creation time and has no honest answer for "I own it but haven't shelved it," "it's in transit," or "I sold it and don't recall where it last sat." Unplaced is a legitimate state, so the constraint is *at most one*, and "Unplaced" is surfaced as a real bucket rather than hidden.

**Stock entries are different.** A unit of fungible stock requires *exactly one* of area / container — a stock entry with no location is not "stock somewhere," it is just a number, which is what the total on-hand already is. That asymmetry is intentional; see [stock.md](stock.md#the-stock-location-rule).

## Containment

A **container** is a non-fungible item whose `IsContainer` role is on. Its contents are:

- **non-fungible items** whose `ParentItemId` is this container, plus
- **fungible stock** whose stock entry's container is this container (rendered at that entry's local quantity).

**Typing invariants** (application-enforced — they span rows and cannot be plain check constraints):

- A `ParentItemId` must reference a **non-fungible** item with `IsContainer = true`. A fungible supply can never be a parent, and a non-container item can never hold contents.
- A `StockEntry.ContainerItemId` must likewise reference a non-fungible `IsContainer = true` item.
- `IsContainer` may be **on only for non-fungible items** — never for a fungible supply.
- `IsContainer` **cannot be switched off** while the item still has any child item or any stock entry pointing at it. Empty it first.

**Nesting and cycles.** Containers nest to any depth (bin in cupboard in garage). Because containment is a parent chain, a move must not create a cycle: an item cannot be placed inside itself or inside any of its own descendants. Moving a container therefore validates that the proposed new parent is not the item itself or anywhere in its subtree, by walking up from the target. This is the one structural guard on moves.

## Resolved location

An item's **resolved area** is derived, never stored:

```
resolveArea(item):
  while item.ParentItemId is set:
    item = parent(item)
  return item.AreaId        # may be null → Unplaced
```

Walk up the parent chain to the first item that sits directly in an area; that area is the answer. If the chain ends at an item that is itself Unplaced (no area, no parent), the whole chain resolves to **Unplaced**. The resolved location may be presented as a path for context (`Garage › Workbench cupboard › Small-parts bin`), but only the leaf item's own one-step location is ever stored.

**Never denormalise this.** Caching a resolved `AreaId` onto nested rows is explicitly disallowed — it is precisely the optimisation that reintroduces move-with-contents drift. The walk is short and cheap at household scale; the codebase already resolves hierarchies in application code rather than via stored rollups or recursive SQL, and Inventory follows that idiom. The *mechanism* (in-memory walk, a recursive query, or a mapped view) is an implementation choice left open; the **invariant — resolved location is derived, never stored — is locked.**

## Move-with-contents

Moving an item changes **only that item's own location pair** — its `AreaId`/`ParentItemId`. Everything inside it follows automatically, because every descendant's and every contained stock entry's resolved area is computed through the chain that now ends at the moved item's new spot. There is no cascade write and no stored value to reconcile.

Concretely, the supported moves are:

- **Into an area** — set `AreaId`, clear `ParentItemId`.
- **Into a container** — set `ParentItemId` (to a valid container, cycle-checked), clear `AreaId`. An optional `Compartment` free-text note ("top drawer") can record sub-position within the parent; it is descriptive only and anchors nothing.
- **Out to Unplaced** — clear both.

A fungible supply is never "moved" as a unit — its stock is transferred between locations entry-by-entry; see [stock.md](stock.md#transfers).

## Non-fungible item lifecycle

An item's `Status` records the state of the physical object. Non-fungible items use the full set; fungible supplies are restricted to `Active`/`Archived` (see [stock.md](stock.md#fungible-status)).

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

### Container emptiness and terminal status

Two operations on a container guard against stranding its contents, and they guard differently:

- **Taking the container terminal** (`Sold`/`Discarded`/`Archived`) is blocked **only by *live* contents** — `Active`/`Lent`/`InRepair` children and any stock entries. Terminal children (an already-sold thing that was in the box) do **not** block it: they are hidden from the default contents view and so cannot strand the parent, and the container's role **stays on** when it goes terminal, preserving the parent-is-container invariant for those leftovers.
- **Switching the container role off** (`IsContainer` → false) is stricter: it is blocked while **any** child item — live *or* terminal — still points at the container (and, later, any stock entry). This is the conservative rule, because turning off the role removes the container concept entirely, and a dangling `ParentItemId` on a sold record would have nothing valid to reference. Empty it first.

The asymmetry is deliberate. A terminal container is still a container (its role is intact), so a terminal leftover inside it remains coherent. A role-off erases the container, so nothing may still be pointing in.

**The escape hatch — terminal records stay movable.** Keeping the role-off rule strict would otherwise dead-end: a container holding only sold/discarded leftovers could never have its role turned off, because those leftovers block it and a terminal item's location looks frozen. It is not frozen. The location lock applies **only to the live→terminal transition patch itself** (so a "sell and move" in one step keeps the item where it was); an **already-terminal** item's `AreaId`/`ParentItemId`/`Compartment` are freely editable afterwards. So the path to emptying such a container is: move each leftover record out (to an area or to Unplaced), then switch the role off.

*Future work:* a one-click "release N inactive records" action could unplace all terminal children of a container in a single step, so a user does not have to move each leftover by hand before turning the role off. Not built in this slice — the per-record escape hatch above is the supported path today.

### Delete vs archive

Inventory keeps the Finances delete-vs-archive split:

- **Archive** (`Status = Archived`) is the keep-the-record path — the item leaves the active picture but its row, history, and any attachments survive. This is the normal way to retire something you no longer want listed.
- **Delete** is the mistake-eraser, and is **disabled once the item carries history worth keeping**: a non-fungible item that is or was a container with contents, or a fungible item with movement-ledger rows, cannot be hard-deleted — it can only be archived. A freshly mis-created item with no history can be deleted outright.

This mirrors `accounts.md`'s rule that Delete is present but disabled once an account has transactions, and the "Show archived (N)" reveal in the same module.

## Area lifecycle

Deleting an area is governed by what references it:

- **Blocked while live items or stock currently sit in it** — directly-placed items with a non-terminal status, and any stock entries in the area. Move or retire them first.
- **Historical movement references survive a deletion.** The movement ledger records the area an inflow/outflow/transfer happened at; those references are cleared (set null) rather than blocking the delete, so the quantity history and what happened to it persist even though the now-deleted area's label is gone.

There is no area archive in v1 (areas carry no lifecycle status); an area is either present or deleted. If a household stops using a room, the practical path is to empty it and delete it, accepting that historical movements that happened there lose their location label. (An area archive/rename-only path is a natural future addition if losing those labels proves annoying.)

## Audit

Every mutation here writes to the shared field-level audit log via the standard logger — area create/rename/delete, item create/edit/move/status-change, container-role toggles. Inventory contributes per-entity snapshots (Area, Item) so the diffs are captured, consistent with every other module. This is separate from, and complementary to, the fungible movement ledger in [stock.md](stock.md): the audit log records *metadata* edits (a rename, a threshold change, a move); the movement ledger records *quantity* history. Neither replaces the other.
