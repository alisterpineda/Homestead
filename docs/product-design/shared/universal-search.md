# Universal Search — Reference

Cross-cutting design reference for Homestead's **global command palette**: a single search box in the
shell header (and ⌘K / Ctrl+K from anywhere) that searches across modules and navigates to the chosen
result. Consult this before adding a new searchable module or changing palette behaviour.

This is product/UX design — *what* the palette does and the seam that keeps it extensible.
Implementation plumbing (the `ISearchSource` contract, the MediatR handler, the Spartan `command`
primitive, the presenter registry) lives in the `Homestead.WebApi/CLAUDE.md` and
`Homestead.WebApp/CLAUDE.md` conventions.

## What it is

- A **search-only** palette (v1). It surfaces *records*, not nav links or actions — there are no
  "go to settings" style commands. Typing ≥ 2 characters runs a search; results are grouped by the
  module they came from.
- **Header affordance:** a faux search input on desktop (`🔍 Search…  ⌘K`) that collapses to an icon
  button on mobile. ⌘K / Ctrl+K opens the palette from anywhere; Esc / overlay-click closes it.
- **Ephemeral:** palette query state lives in memory only. It does not sync to the URL — that belongs
  to each module's own list page (see *Coexistence*).

## Grouped, not globally ranked

Results are returned and rendered as **one group per source** (e.g. an "Inventory" group), each ranked
*within itself*. There is deliberately **no global cross-source relevance ranking** — comparing a
"how good a match" score across a document, an inventory item, and a transaction is not meaningful, and
a household-scale result set is small enough that grouping reads more clearly than a merged list.

Each group shows up to a small cap of hits (default 6). When a source has more matches than the cap, a
trailing **"View all (N) in {module}"** row deep-links into that module's own list page, pre-filtered to
the query.

## Coexistence with module search

The palette is a global *jump-to*. It does **not** replace the in-page search each module's list page
already has — those keep their own `?search` URL param and richer filters, untouched. The palette's
"View all" link is the bridge: it hands the query off to the module list (e.g.
`/inventory/items?search=…`), where the user gets the full filtered table.

## The extension seam (universal from day one)

The palette ships wired to a **single source — Inventory items** — but is built so other modules become
*additive*: no change to the shell, the header, or the search endpoint.

- **Backend:** each module implements one `ISearchSource` (its own query + how to shape a result). The
  search endpoint fans out to every registered source and returns per-source groups. Adding a module =
  implement one source + register it.
- **Frontend:** each source has one entry in a **presenter registry** keyed by the source's machine
  name — it owns that source's result icon, the route to a result's detail, and the "View all" deep
  link. Routing stays frontend-side. Adding a module = register one presenter.

A result carries a source-internal `type` (e.g. `durable` / `consumable`) that the presenter uses to
pick an icon and route, so one source can render heterogeneous result kinds.

## Inventory matching (the v1 source)

Matches an item's **own text** — Name + Serial + Unit + Compartment + Notes (single table, no joins) —
via case-insensitive **substring** match, plus a **fuzzy fallback on Name only** (pg_trgm
`word_similarity`, threshold 0.3) so typos and plural mismatches still surface (`battry` → battery,
`boxs` → box). Archived items are excluded. A durable's subtitle is its
direct location (area, else containing item, else "Unplaced"); a consumable's subtitle is its on-hand
total with unit; a status/low-stock badge shows when relevant.

## Out of scope (v1)

- Nav / action commands in the palette (search results only).
- Sources other than Inventory items (Documents, Finances, Admin) — additive later via the seam above.
- Global cross-source ranking (a deliberate non-goal, not a deferral).
