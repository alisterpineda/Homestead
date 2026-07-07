# Stock & Movements — Reference

Detailed design for the fungible side of Inventory: stock entries, the movement ledger, thresholds, the conversion operations, and how quantity changes stay consistent. Expands on the summaries in [overview.md](overview.md#stock-and-consumables). Read this before designing, implementing, or reviewing anything that reads or writes fungible quantity.

Areas, containment, resolved location, and the non-fungible item lifecycle live in [locations.md](locations.md); this document treats them only where stock sits inside a container or where lifecycle gates a stock operation.

## Conceptual model

A **fungible item** is a *type* of consumable, not an instance. It owns no single location and no single quantity. Instead:

- its presence at each location is a **stock entry** — `(item, location, quantity)`;
- its **total on-hand** is the sum of those entries' quantities;
- its history is the **movement ledger** — an append-only record of every quantity change.

Current state (stock entries) and history (movements) are written together, by one service, so the live quantity is always exactly what replaying the ledger would produce.

## Stock entries

A **stock entry** records how much of one fungible item sits at one location.

| Field | Notes |
|---|---|
| `Id` | uuid |
| `ItemId` | the fungible item |
| `AreaId` | location: an area … |
| `ContainerItemId` | … or a container item — exactly one of the two |
| `Quantity` | how much, in the item's unit; `>= 0` |
| `CreatedAt`, `UpdatedAt` | UTC |

### The stock-location rule

Exactly **one** of `AreaId` / `ContainerItemId` is set (enforced by a check constraint in the house "not-both / not-neither" style). A `ContainerItemId` must reference a non-fungible item with `DurableKind = Container` — loose stock sits only in a storage place, never in an assembly — matching the containment typing rules in [locations.md](locations.md#containment-vs-composition).

Note the deliberate asymmetry with non-fungible items, which may be **Unplaced** (no location). Stock may not: a stock entry exists *because* there is quantity at a place. "I have some, location unknown" is not a placeless entry — it is simply the total on-hand carried by the entries that do have a place. Removing the last located entry leaves a total of zero, which is the correct representation of "none here."

### One entry per item per location

There is at most one stock entry per `(item, area-or-container)` — a **unique constraint on `(ItemId, AreaId, ContainerItemId)`**. Adding stock at a location where the item already sits **merges** into the existing entry (quantity adds); it never creates a second row. Because two of the three columns are nullable and Postgres treats nulls as distinct by default, the constraint must use `NULLS NOT DISTINCT` (or be expressed as two partial unique indexes, one per location kind) so the merge rule actually holds.

### Zero-quantity entries are removed

When an entry's quantity reaches zero (everything at that location was used, transferred away, or carved out), the **entry is deleted**, not left lingering at zero. The history of what happened there survives in the movement ledger; the stock entry table holds only places that currently have something. This keeps "which locations stock this item" honest and keeps `SUM(Quantity)` free of zero rows.

## Total on-hand and low stock

- **Total on-hand** for a fungible item = `SUM(Quantity)` over its stock entries.
- **Low stock**: the item is low when `total <= RestockThreshold`. `RestockThreshold` is **optional** (nullable) — not every consumable has a meaningful reorder point — and is **global to the item**, compared against the total, not against any single location. (Per-location thresholds are a named future addition; see [overview.md](overview.md#future-compatibility).)

Low-stock is a derived read, surfaced as an inline badge on consumable rows in the unified items list, as its own "Low stock only" filter (the de-facto reorder view), and as a tile on the `/inventory` overview landing page — an exact household-wide count with the most urgent consumables previewed first (lowest on-hand-to-threshold ratio; a zero threshold ranks most urgent, since it only matches at zero on-hand) and a deep link into that filter; nothing is stored as "is low." Consumables appear in that list alongside non-fungible items — one row per fungible type showing total on-hand and resolved locations — with an `All | Durables | Consumables` kind filter standing in for the old separate consumables page. Because each stock entry resolves to an area or container, consumable rows participate in the list's area filter (showing each type's total), and when a place is drilled in **Browse** they show the local quantity with an "of N total" hint.

## The movement ledger

Every change to fungible quantity appends one **movement** row, written in the same transaction as the stock-entry change.

| Field | Notes |
|---|---|
| `Id` | uuid |
| `ItemId` | the fungible item |
| `Kind` | `Flow` \| `Adjustment` \| `Conversion` (string-backed) |
| `FromAreaId` / `FromContainerItemId` | source location (nullable) |
| `ToAreaId` / `ToContainerItemId` | destination location (nullable) |
| `Quantity` | positive magnitude of the change |
| `UnitCost` / `UnitCostCurrencyId` | optional cost, with its currency — both or neither |
| `OccurredAt` | when it happened (UTC; back-dating allowed) |
| `CreatedByUserId` | who recorded it (nullable; cleared if that user is deleted) |
| `RelatedItemId` | for `Conversion`: the non-fungible item carved out / collapsed (nullable) |
| `Note` | free text (nullable) |

### Direction is structural

A movement's direction is **read from its location fields**, not stored as a type:

| Source | Destination | Direction |
|---|---|---|
| null | set | **inflow** — stock came in |
| set | null | **outflow** — stock left |
| set | set | **internal transfer** — moved between locations (nets to zero on-hand) |

So `Purchase`, `Consume`, and `Transfer` are not enum values — they fall out of which location fields are populated. This avoids carrying a type that merely duplicates the location structure.

### Kind separates real flow from bookkeeping

What the location structure *cannot* tell you is whether an outflow is genuine consumption or a paperwork artefact. Three movements all look like "outflow, no destination" — using a roll, recounting and finding one fewer, and carving a unit out to track individually — but only the first is consumption. The `Kind` discriminator captures exactly that distinction, and nothing more:

- **`Flow`** — a genuine acquisition or consumption. Buying stock (inflow) and using stock (outflow) are both `Flow`; a transfer (both ends set) is also `Flow`. These are real-world stock events.
- **`Adjustment`** — a recount/correction reconciling the recorded number to physical reality. Excluded from consumption analytics so a stocktake never inflates burn rate.
- **`Conversion`** — the quantity moved because of *track-this-one-individually* or its reverse, not because anything was bought or used. Carries `RelatedItemId`. Also excluded from consumption.

Every `Kind` value earns its place by changing what analytics count; there is no value that is merely descriptive.

### The usage query

Consumption over a period is precisely:

```
Kind = 'Flow' AND source is set AND destination is null
```

— Flow outflows. Inflows (Flow, destination only) are acquisitions; transfers (Flow, both set) net to zero and are excluded automatically; `Adjustment` and `Conversion` are excluded by kind. This is the query that makes "how fast are we going through this" trustworthy, and it is the reason the kinds exist.

### One movement per gesture, not per click

The consumables quick panel changes stock primarily through per-location **− / + steppers**. Taps are debounced, and each burst commits as **one `Flow` movement of the net delta** — tapping + three times records a single inflow of 3, not three inflows of 1; a − then a + in the same burst nets to zero and **writes nothing at all**. A net-positive burst lands as one receive (inflow), a net-negative burst as one consume (outflow); "Correct" remains a distinct action and still writes an `Adjustment`.

This is deliberate ledger-granularity policy, not a UI accident: a ledger row should record a household event at the granularity of user *intent* ("used 3 rolls"), so rapid clicking never fragments history into click-sized rows or inflates the movement count that burn-rate analytics read. The coalescing happens before the write — the single-service funnel below still receives ordinary receive/consume commands and is unaware of it.

The movement ledger is **distinct from the system audit log**, and both are kept:

- The **audit log** (shared `auditing` infrastructure) records field-level "who changed what" across every module — a rename, a threshold edit, a unit change — as un-aggregated diffs. Inventory contributes snapshots for its entities like every other module.
- The **movement ledger** is typed domain data, queried and summed for on-hand reconstruction and consumption. It is not a duplicate of the audit log and is not derived from it.

A metadata edit (renaming a consumable) writes the audit log only; a quantity change writes the ledger *and* (via the stock-entry mutation) the audit log. They answer different questions.

## All quantity changes go through one service

Every stock mutation — receive, consume, transfer, adjust, and the conversions below — flows through a **single application service**. The UI never writes a stock entry directly. The service, for each change:

1. applies the quantity delta to the relevant stock entry (creating, merging, or deleting it per the rules above), and
2. appends the matching movement row,

in **one transaction**. Atomicity is already guaranteed by the codebase's per-command transaction pipeline — the service does not open its own transaction — so the funnel exists for *consistency of code path*, not to provide atomicity: there is exactly one place that knows how to keep stock entries and the ledger in agreement. (This mirrors how Finances routes all trade/transaction writes through dedicated services.) The single funnel is also what keeps the door open to a future switch to derived-on-hand with no UI change.

### Quantities are deltas, applied atomically

Stock changes are applied as **deltas** (`quantity = quantity - n`), not as absolute overwrites, wherever the operation is naturally a delta. This is what keeps the ledger-replay invariant true and is the cheap groundwork for eventual offline/sync.

**Concurrency.** Inventory does not use optimistic concurrency (no row-version/`xmin` token) — consistent with the rest of the codebase, which is last-write-wins throughout. For metadata that is fine. For quantities, correctness comes not from a conflict token but from applying the change as an **atomic database-level decrement** and backing it with a **`Quantity >= 0` check constraint**: two concurrent "use one" operations both decrement rather than both overwriting a stale read, and an over-consume that would drive the quantity negative is rejected by the constraint rather than silently losing a movement. This gives quantity correctness without inventing a 409-conflict retry UX that exists nowhere else in the app.

One narrow exception: a rare **uniqueness-collision** race — an inflow landing at a location whose entry was concurrently purged to zero (the entry is re-inserted so the movement never vanishes), or two simultaneous first-receives at one location — can lose the `(ItemId, AreaId, ContainerItemId)` unique-index race and degrade to a **retryable 409**. This is a genuine uniqueness collision (the same 409 the app already returns for duplicates elsewhere), not a routine optimistic-concurrency retry UX — there is still no row-version token, and the everyday decrement/adjust paths retry internally rather than surfacing a conflict.

### Validation

- A consume or transfer may not exceed the quantity on-hand at the source location — over-consume/over-transfer is rejected (with the `>= 0` constraint as the last-resort backstop).
- Movement `Quantity` is a positive magnitude; direction comes from the location fields, never from a signed quantity.
- `UnitCost` and `UnitCostCurrencyId` are set together or not at all; the currency references the seeded, read-only currency set (see [overview.md](overview.md#every-amount-carries-its-currency)).

## Transfers

Moving stock between two locations is a single operation that decrements the source entry and increments (or creates) the destination entry, writing **one** `Flow` movement with both `From*` and `To*` set. It nets to zero on-hand, so it never appears in consumption. If the source entry hits zero it is removed; if the destination location already had stock the quantities merge.

## Fungible status

Fungible items use a reduced lifecycle — only **`Active`** and **`Archived`**. `Lent`, `InRepair`, `Sold`, and `Discarded` are meaningless for a count of a consumable (you do not lend "screws" as an identity). Archiving a consumable removes it from the default unified items list behind the same "Show inactive" reveal used elsewhere; per the delete-vs-archive rule in [locations.md](locations.md#delete-vs-archive), a fungible item with any movement history can only be archived, not hard-deleted. The consumable detail page exposes **Archive** (and **Restore** once archived), plus **Delete** when there is no ledger history, through its `⋮` overflow menu — the same lifecycle actions the unified list's row actions offer.

## Track this one individually

Carving a single tracked instance out of bulk stock, in one transaction:

1. **Decrement** the fungible stock by 1 at the chosen location (removing the entry if it hits zero).
2. **Append a `Conversion` movement** — an outflow of 1 at that location — with `RelatedItemId` pointing at the new item.
3. **Create a non-fungible item** at the **same location** (the carved-out instance inherits where it came from), in `Active` status.

The result is lossless and atomic: the count drops by one, a new individually-tracked thing appears where the stock was, and the ledger explains the change as a conversion rather than consumption (so it never pollutes burn rate). It is offered **only when the source location holds a whole number of units (≥ 1)** — carving "one" out of `2.5 kg` of flour is meaningless. There is no per-item "count-like" flag: a whole-number on-hand is the proxy. The backend enforces the same rule (rejecting fractional or below-one source quantities); the UI simply hides the action when the row's quantity is not a whole number. Traceability runs both ways: the movement's `RelatedItemId` links the stock change to the instance it produced.

## Collapse back to a count

The reverse — folding several individually-tracked instances back into a fungible count:

1. **Increment** the fungible stock by N at the target location.
2. **Append N `Conversion` inflow movements of quantity 1** — one per collapsed instance, each carrying its own `RelatedItemId`. Traceability therefore runs both ways here too, mirroring *track-this-one-individually*: every collapsed instance leaves its own ledger row pointing back at the item it came from, rather than a single anonymous inflow of N.
3. **Archive** the collapsed non-fungible instances (do **not** hard-delete them), keeping their location.

Only **`Active`** instances are eligible: `Lent` / `InRepair` ones are physically elsewhere, so folding them back would inflate on-hand and silently drop their `LentTo`. A container can be collapsed only once it is empty (no live children, no stock inside) — the same sealing gate that guards taking a container terminal — and it cannot be its own collapse target.

This is mechanically simple but **lossy**: per-instance serials, values, compartments, and individual history have nowhere to live on a bulk count. So the operation **warns** before proceeding, and it **archives** the source instances rather than deleting them, so their records and any attachments survive for later reference. The asymmetry with *track-this-one-individually* (which is lossless) is intrinsic: splitting adds identity, collapsing discards it.

## Future-compatibility

Addable later without re-migrating stock or movements:

- **Derived on-hand** — computing the live quantity from the ledger instead of keeping it on the stock entry. All mutations already funnel through one service, so this is an internal change with no UI impact.
- **Per-location restock thresholds**, alongside or replacing the single global threshold.
- **Cost and consumption reporting** built on the ledger's `Flow` movements and captured `UnitCost` — both recorded from day one for exactly this purpose.
- **Cross-module cost/value rollup** into Finances — `UnitCost` and item value already carry their currency.

None of these should require re-migrating historical stock or movement rows.
