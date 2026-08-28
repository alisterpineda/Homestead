# Tags — design

Tags are the Inventory module's classification hierarchy — the "what kind of thing is this / what cross-cutting groups does it belong to" axis, and the one classification that is multi-valued. They **replace the deferred single-valued Category taxonomy**: rather than a flat category lookup, items are organised by hierarchical tags. This file covers the path model, colour inheritance, the cascade on edit and delete, how tags participate in the items-list filter, and inline find-or-create on the item forms. The high-level picture lives in [overview.md](overview.md); load this when working on tag CRUD, the tag filter facet, colour rendering, or the item-form tag input.

Tags are a faithful port of the Documents tag subsystem ([../documents/tags.md](../documents/tags.md)) into Inventory, minus the Documents-specific parts (per-document access control, ZIP-download organisation, tag-name matching in full-text search). The mechanics below — path identity, `ParentId`, `Color`/`EffectiveColor` cascade — are shared; the differences are called out where they matter.

## Path-delimited hierarchy

A tag has a name and a slash-delimited **path** that doubles as its identity. `Electronics/Batteries/AA` is a single tag whose path encodes its position in the hierarchy. When the user creates or applies the path `Electronics/Batteries/AA`, all ancestors (`Electronics`, `Electronics/Batteries`) are upserted in the same operation — there is no separate "create root, then add child" workflow.

This means:

- Tag identity is the path, not a `(name, parent)` pair. Renaming a tag is a path change.
- Path uniqueness is global: there is only one `Electronics/Batteries` per household.
- Parent/child is *derived* from the path on write — a `ParentId` foreign key is set so descendant queries are cheap. The path is the source of truth **on create and upsert**, where the user supplies the full path. On **edit** the direction reverses: the leaf `Name` and `ParentId` are the inputs and the path is re-derived from them (see *Create / edit / delete*).

Tags apply to **both item kinds** — non-fungible durables and fungible consumables — through one many-to-many join (`ItemTag`). Items are referenced by id at the join level, so an item keeps its tag links across path renames without rewriting any join row.

## Colour and effective colour

Each tag has two colour fields:

- **`Color`** — the colour the user explicitly set on this tag. May be null.
- **`EffectiveColor`** — the colour rendered for this tag everywhere in the UI. Computed as: this tag's `Color` if non-null, else its parent's `EffectiveColor`, else null.

Effective colour is materialised on the tag row, not computed at read time. The trade-off is that list and badge rendering reads `EffectiveColor` directly with no lookup, at the cost of a recomputation step whenever a tag's colour, parent, or position changes.

Colour is opt-in: a tag with no ancestor colour renders as a colourless badge, not a default colour.

## Cascade on edit and delete

Whenever a tag's own colour changes, its parent changes (path move), or it is deleted, the effective-colour chain must be repaired for every descendant. The module does this in a single pass driven by `InventoryEffectiveColorResolver`:

- **Set or clear `Color`** — recompute this tag's `EffectiveColor`, then recompute every descendant whose `EffectiveColor` was inherited through this tag.
- **Move (path change)** — the same recomputation runs against the new parent's `EffectiveColor`. A move is requested by changing the moved tag's leaf `Name` and/or `ParentId`; the handler then re-derives `Path` for the moved tag and for every descendant.
- **Delete** — the tag row is removed, every `ItemTag` join referencing it is removed (items are **un-tagged**, never deleted), and former descendants are **re-parented** to the deleted tag's parent with their effective colour recomputed against the next surviving ancestor.

Path moves are not naive string replacements: the resolver re-walks the new parent chain so a tag promoted to root or moved under a new parent picks up that subtree's colour rules cleanly.

## Tag participation in the items filter

The two item-list queries (durables and consumables are fetched separately and merged client-side) each accept a set of tag ids and a boolean `IncludeChildTags`:

- `IncludeChildTags = false` — items are matched only against the exact selected tags.
- `IncludeChildTags = true` — the selected tags expand to include every descendant by path prefix (`Path = selectedPath OR Path LIKE selectedPath/%`). A query for `Electronics` with descendant expansion returns items tagged `Electronics`, `Electronics/Batteries`, `Electronics/Batteries/AA`, etc.

Multiple selected tags are OR'd, and the filter is applied **server-side on both kinds** (the same expanded id set feeds the durable and consumable queries), so a tag filter composes with the area facet and search without a client-side second pass.

Unlike Documents, inventory tags are **not** wired into item text search or the ⌘K command palette in v1 — they are a **filter facet only**, not a contributor to search relevance and not matched by the search bar. (Documents federates a tag-name ILIKE into its search; Inventory deliberately does not in v1 — additive later, mirroring how Brand is facet-only.)

## Create / edit / delete

- **Create** — a user creates a tag by providing its full path, an optional colour, and an optional description. Missing ancestors in the path are created in the same operation. Creation is idempotent on the path: re-submitting an existing path returns the existing tag, updating the `Color` and `Description` that are supplied and leaving omitted (null) fields untouched.
- **Edit** — the editable inputs are the leaf `Name`, `ParentId`, `Color`, and `Description` (there is no direct `Path` field on the edit model). Changing `Name` or `ParentId` moves the tag: the handler re-derives its `Path` and every descendant's `Path`, and propagates the effective-colour cascade. Renames have no special history record on the tag row; the audit log captures the change (audit label `InventoryTag`).
- **Delete** — destructive. Removes the tag, every `ItemTag` join, re-parents former children, and triggers the cascade. Items are **not** deleted, only un-tagged. There is **no in-use block**: a tag is deletable even while items carry it (contrast Brand, whose in-use reference restricts deletion). There is no soft delete for tags.

Bulk delete on tags has the same semantics as repeated single deletes, applied in one transaction (deepest-first so re-parenting cascades correctly when a parent and child are deleted together).

**No access-control guards.** Inventory data is household-global (like the rest of the module), so tag edit and delete carry none of the Documents per-document ACL checks — any contributor may edit or delete any tag.

## Inline find-or-create on item forms

Tags are applied to items two ways, both path-based:

- **On the item forms** (the new-item dialog and the durable/consumable edit sheets), a chip input autocompletes existing tag paths and offers "Create 'X'" for a new path — full parity with the Documents tag input (on the new-item dialog that input sits under the collapsed **"More details"** section, one tap off the fast path — still no detour to a tag-admin page). Submitting an item sends its tag **paths**; the server resolves-or-creates each (upserting ancestors) and replaces the item's tag set with exactly those (whole-set replace: an omitted tag field leaves the set untouched, a present one replaces it entirely).
- **On the dedicated CRUD page** (`/inventory/tags`), tags are created, renamed, recoloured, re-parented, and deleted (single and bulk) independently of any item.

The item create/patch endpoints accept tag **paths**; the filter facet works in tag **ids** and resolves chip labels through a `/resolve` endpoint (exactly as Documents does).

## UX rules

- **Tag CRUD lives on its own page** (`/inventory/tags`), reached from the sidebar's "Manage" group beside Areas. The items list only references tags as filter selections and read-only badges; renaming and recolouring is intentional, not incidental. The tag table's name cell links into `/inventory/items?tagIds=<id>`, cross-navigating to the items list filtered by that tag.
- **Tag colour is rendered everywhere a tag appears** — list rows, quick panels, detail pages, the filter popover, and the create/edit surfaces. Setting a colour on a parent automatically colours every uncoloured descendant.
- **Child expansion is a property of the filter set, not of individual tags** — the items list exposes a single "Include sub-tags" toggle alongside the multi-select tag picker, rather than per-tag controls. The picker is an inline checklist inside the refine popover (not a nested combobox), and clicking a tag badge on a **detail page, a quick panel, or the tag table** deep-links to `/inventory/items?tagIds=<id>` with the filter applied and the param stripped. On the dense items-list rows the badges are read-only chips (the row itself is the click target — it opens the item), so tag-driven navigation there goes through the refine facet rather than the badge.
