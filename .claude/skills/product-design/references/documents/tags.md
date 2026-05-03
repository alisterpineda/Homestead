# Tags — design

Tags are the Documents module's organisational hierarchy. This file covers the path model, colour inheritance, the cascade behaviour on edit and delete, how tags participate in search, and the role tags play in ZIP downloads. The high-level picture lives in [overview.md](overview.md); load this when you are working on tag CRUD, search expansion, colour rendering, or download organisation.

## Path-delimited hierarchy

A tag has a name and a slash-delimited **path** that doubles as its identity. `Finances/Investments/2024` is a single tag whose path encodes its position in the hierarchy. When the user creates or applies the path `Finances/Investments/2024`, all ancestors (`Finances`, `Finances/Investments`) are upserted in the same operation — there is no separate "create root, then add child" workflow.

This means:

- Tag identity is the path, not a `(name, parent)` pair. Renaming a tag is a path change.
- Path uniqueness is global: there is only one `Finances/Investments` per household.
- Parent/child is *derived* from the path on write — a `ParentId` foreign key is set so descendant queries are cheap, but the path is the source of truth.

Tags are referenced by id at the join level (`DocumentTag`), so a document keeps its tag link across path renames without rewriting the join row.

## Colour and effective colour

Each tag has two colour fields:

- **`Color`** — the colour the user explicitly set on this tag. May be null.
- **`EffectiveColor`** — the colour rendered for this tag everywhere in the UI. Computed as: this tag's `Color` if non-null, else its parent's `EffectiveColor`, else null.

Effective colour is materialised on the tag row, not computed at read time. The trade-off is that list and badge rendering reads `EffectiveColor` directly with no lookup, at the cost of a recomputation step whenever a tag's colour, parent, or position changes.

Colour is opt-in: a tag with no ancestor colour renders as a colourless badge, not a default colour.

## Cascade on edit and delete

Whenever a tag's own colour changes, its parent changes (path move), or it is deleted, the effective-colour chain must be repaired for every descendant. The module does this in a single pass driven by `EffectiveColorResolver`:

- **Set or clear `Color`** — recompute this tag's `EffectiveColor`, then recompute every descendant whose `EffectiveColor` was inherited through this tag.
- **Move (path change)** — the same recomputation runs against the new parent's `EffectiveColor`. Path moves also re-derive `Path`, `Name`, and `ParentId` for the moved tag and re-derive `Path` for all descendants.
- **Delete** — the tag row is removed, every `DocumentTag` join referencing it is removed, and former descendants' effective colour is recomputed against the next surviving ancestor.

Path moves are not naive string replacements: the resolver re-walks the new parent chain so a tag promoted to root or moved under a new parent picks up that subtree's colour rules cleanly.

## Tag participation in search

The list query accepts a set of tag ids and a boolean `IncludeChildTags`:

- `IncludeChildTags = false` — documents are matched only against the exact selected tags.
- `IncludeChildTags = true` — the selected tags expand to include every descendant by path prefix (`Path = selectedPath OR Path LIKE selectedPath/%`). A query for `Finances` with descendant expansion returns documents tagged `Finances`, `Finances/Investments`, `Finances/Investments/2024`, etc.

Multiple selected tags are OR'd. Tags do **not** contribute to the FTS `tsvector` — they are a structural filter, not a contributor to relevance ranking. See [search.md](search.md) for how tag filters interact with search and sort.

## Tag participation in ZIP download

A bulk download (`DownloadDocumentsCommand`) groups documents in the resulting ZIP by their tags' full paths:

- A document with one tag `Finances/Investments` is placed at `Finances/Investments/<filename>`.
- A document with two tags appears in **both** folders with duplicated content. The duplication is intentional: each tag is an independent organisational view of the same document.
- A document with no tags is placed in a top-level `_Untagged/` folder.
- Linked party names are appended to the filename (`<docname> - <party1>, <party2><ext>`), truncated past 200 characters.
- Within any folder, filename collisions are resolved by appending a counter (`file 1.pdf`, `file 2.pdf`).

ZIP organisation is the only place where the *full tag path* is materialised in user-visible output. Everywhere else (badges, filters), tags are referred to by their leaf identity with the path implicit in their colour and structure.

## Create / edit / delete

- **Create** — a user creates a tag by providing its full path, an optional colour, and an optional description. Missing ancestors in the path are created in the same operation. Creation is idempotent on the path: re-submitting an existing path returns the existing tag without resetting its other fields.
- **Edit** — `Path`, `ParentId`, `Color`, and `Description` are all editable. Path changes propagate the effective-colour cascade to descendants. Renames have no special history record on the tag row; the audit log captures the change.
- **Delete** — destructive. Removes the tag, every `DocumentTag` join, and triggers the cascade on former descendants. Documents are not deleted. There is no soft delete for tags.

Bulk delete on tags has the same semantics as repeated single deletes, applied in one transaction.

## UX rules

- **Tag CRUD lives on its own page** (`/documents/tags`), separate from the documents list. The list page only references tags as filter chips and badges; renaming and recolouring is intentional, not incidental.
- **Tag colour is rendered everywhere a tag appears** — list rows, detail page, filter popover, edit dialogs. Setting a colour on a parent automatically colours every uncoloured descendant.
- **Child expansion is a property of the filter set, not of individual tags** — the list page exposes a single toggle for "include descendants" alongside the tag picker, rather than per-tag controls.
