# Brands — design

A **Brand** is the Inventory module's canonical manufacturer lookup — the "who made it" axis, single-valued per item and shared across both item kinds. This file covers the entity, the unchanged entry-time find-or-create, the dedicated management page (rename / delete / merge), why delete is guarded where Tags' delete is not, the merge semantics and how they're audited, and the rename-collision → merge handoff. The high-level picture lives in [overview.md](overview.md); load this when working on brand CRUD, the merge flow, or the brand picker. Where Area answers "where is it" and Tags answer "what is it / what groups is it in," Brand answers "who made it" — the three are orthogonal.

## The entity

`Brand` is a flat, first-class row: an `Id`, a `Name`, and `CreatedAt` / `UpdatedAt` timestamps. It has **no parent, no status, no user attribution, no colour** — deliberately the simplest of the three lookups (contrast the `Area` tree and the `Tag` colour/hierarchy machinery). Both durables and consumables reference it through **one optional foreign key on the shared item row** (`Item.BrandId`, `OnDelete: Restrict`), so a brand is single-valued per item and applies to both kinds. There is no reverse navigation collection on `Brand` by design — "how many items use this brand" is a query-time correlated count (`db.Items.Count(i => i.BrandId == b.Id)`), not a materialised field.

Canonicalisation rests on **entry-time find-or-create plus a database unique index on `lower(Name)`**. There is no per-item free-text brand and no brand-as-tag; the shared row is the single source of truth for a maker's identity.

## Entry-time find-or-create (unchanged)

The primary way brands come into being is still the **`<app-brand-picker>` combobox on the item forms** (the new-item dialog and the durable/consumable edit sheets). It autocompletes existing brands via a server search and offers "Create 'X'" for a name that doesn't yet exist. Creation is **idempotent and case-insensitive**: typing "apple," "Apple," or " apple " all resolve to the one canonical row (the handler trims + lower-cases and returns the existing row on a hit; the unique `lower(Name)` index is the backstop). This path is untouched by the management page — attributing a brand while cataloguing an item never requires a detour to a separate admin step.

The picker takes three opt-in inputs that default to the item-form behaviour: `allowCreate` (true — offer the "Create 'X'" and "No brand" rows), `excludeId` (none), and `initialSearch` (empty). The merge dialog opts out of the first two and seeds the third (below).

## The management page

A dedicated **Brands management page** (`/inventory/brands`, in the sidebar's "Manage" group beside Items / Areas / Tags) owns the deliberate operations entry-time find-or-create can't provide. It is a search / sort / paginated table (brands scale like tags, not like the small flat Areas set), with fixed columns — **Name** (a link into `/inventory/items?brand=<id>`, matching the item-detail chip), **Items** (the query-time count), and **Updated** — and a per-row **⋮ menu**. There is **no selection / bulk-delete** and no column-visibility control: the operations are per-row and deliberate.

The **⋮ menu** offers **Rename**, **Merge into…**, and **Delete**. Delete is disabled and tooltipped with the in-use count ("In use by N items — merge instead") whenever the brand's item count is > 0.

Like the rest of Inventory, brands carry **no access-control guards** (household-global) beyond the module's contributor requirement: reads are open to any active user; rename / delete / merge require the contributor role.

## Delete blocks when in use (contrast Tags)

Deleting a brand is **guarded**: a brand referenced by any item **cannot be deleted**. The handler checks `db.Items.Any(i => i.BrandId == id)` and throws a business-rule error (→ 400) with a message pointing at merge, ahead of the FK `Restrict` backstop that would otherwise surface as a raw constraint violation. An unused brand deletes cleanly (→ 204).

This is the **opposite** of Tags, whose delete is destructive with **no in-use block** (deleting a tag un-tags every item and re-parents its children). The difference is intentional and follows the data model: a brand is a single-valued FK whose reference *restricts*, so a stale brand label can't be silently cleared the way a many-to-many tag link can — and losing "which maker made this" is a more meaningful loss than losing one of several cross-cutting classifications. **Merge is the retire path** for an in-use brand.

## Merge

**Merge is the load-bearing operation** — the one job entry-time find-or-create fundamentally cannot do: consolidate *true* duplicates that differ by more than casing ("Apple" / "Apple Inc"). Rename can't do it either — renaming onto an existing name hits the unique `lower(Name)` index and returns a **409**.

`MergeBrand(sourceId, targetId)` re-points **every** `Item.BrandId` from the source onto the target, then deletes the source row. The re-point is a single bulk `ExecuteUpdate` (the `DeleteGroupHandler` FK-reassign precedent), which shares the command transaction automatically with the tracked source deletion — the two are atomic without a manual transaction. Self-merge (`source == target`) is rejected (400); a missing source or target is a 404. The response returns the **target** with its refreshed item count.

**Audit:** the brand **deletion** is audited (`audit.Deleted("Brand", source.Id, source.Name)`). The per-item `BrandId` change is **not** field-audited — matching the `DeleteGroup` precedent, which nulls document `GroupId` via the same bulk update without auditing each document. The meaningful, queryable event is that the source brand was retired into the target.

## The rename-collision → merge handoff

Because a rename onto an existing name is a 409, the **rename (edit) dialog turns that collision into a merge offer** rather than a dead-end toast. On a 409 the dialog surfaces an inline "A brand named 'X' already exists — merge into it?" with a **Merge** button. Confirming hands off to the merge flow: the edit dialog closes and the **merge dialog opens pre-seeded** — source is the brand being renamed, and the target picker's search is seeded with the attempted name so the existing brand surfaces immediately. This makes "I typed a name that already exists" resolve to the correct outcome (consolidate onto the canonical row) in one continuous flow, instead of forcing the user to cancel, find the duplicate, and start over.

## UX rules

- **Brand CRUD lives on its own page** (`/inventory/brands`), reached from the sidebar's "Manage" group after Tags. The items list references brands only as a filter facet and a read-only detail chip; renaming, deleting, and merging are intentional, not incidental. The brand table's Name cell links into `/inventory/items?brand=<id>`, cross-navigating to the items list filtered by that brand — the same destination as the item-detail brand chip.
- **Delete is gated on the item count**, disabled + tooltipped in the ⋮ menu when the brand is in use, and — should the request be attempted anyway — rejected server-side with the reason shown **inline in the confirm dialog** (not a toast the dialog overlay would obscure).
- **Merge is reachable two ways** — explicitly from the ⋮ **Merge into…** menu (open target picker, no seed), and implicitly from the rename-409 handoff (pre-seeded to the attempted name). Both funnel through the same destructive-confirm merge dialog, which spells out the consequence ("Move N item(s) from 'source' into 'target', then delete 'source'. This can't be undone.").
- **The merge target picker reuses `<app-brand-picker>`** with `allowCreate=false` (you merge into an existing brand, never create or clear) and `excludeId=<source>` (you can't merge a brand into itself).
