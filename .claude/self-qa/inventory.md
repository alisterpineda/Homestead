# Self-QA reference — Inventory (`/inventory`, InventoryLayoutComponent; index → browse)

| Route | Component |
|---|---|
| `/inventory/browse` | InventoryBrowsePageComponent — browse-by-place area grid; header breadcrumb via PageHeaderService when drilled |
| `/inventory/browse/area/:areaId` | same component, drilled area (instance reused) |
| `/inventory/browse/container/:containerId` | same component, drilled container; "Details" button routes out to `/inventory/items/:id` |
| `/inventory/browse/unplaced` | same component, unplaced items |
| `/inventory/items` | InventoryListPageComponent — unified durables + consumables list; search/filter, container drill |
| `/inventory/items/:id` | ItemDetailPageComponent — the unified detail page for **both** kinds (header breadcrumb `Items › {name}`; containers show a Contents drill) |
| `/inventory/areas` | AreaListPageComponent |
| `/inventory/tags` | TagListComponent |
| `/inventory/brands` | BrandListComponent |
| `/inventory/consumables` | redirects → `/inventory/items` |

- There is **no `/inventory/supplies` route** and no `SupplyDetailPageComponent` — navigating there
  throws `NG04002: Cannot match any routes` and dumps you at `/`.
- Browse drill nodes share one component; the URL names only the current node, the ancestor trail
  comes from the API's `PlaceContentsResponse.path`.
- **`/inventory/items` rows do not navigate.** Clicking a row opens the item's **quick-panel
  dialog**; reach `/inventory/items/:id` via that panel's "Details" action or a direct URL.
- **Re-parenting an area is behind "Rename", not a "Move".** The area row's actions menu offers only
  Rename and Delete; Rename opens a dialog titled **Edit area** carrying the `Parent area` picker
  ("Moving an area takes its contents with it"). This is the only straightforward way to restructure
  an area tree — see the API note on why not over HTTP.

## Activity feed (both detail pages)

Shared `app-item-activity-feed`: one list of audit rows interleaved with user notes, no toggle and
no filter (the Documents stream shape). Load fires a single `GET …/activity?Page=1&PageSize=20`. The
composer is one row: a date chip (default "Today", sticky across submits) + a single-line input
(placeholder `Add a note...`) with an inline `Add note` send button; Enter submits. ⋮ → Edit swaps
the row for the same composer with Cancel / Save instead of send (Escape cancels) and disables the
header composer; ⋮ → Delete confirms in an alert dialog. Nothing is stored in `localStorage`.

## API

- Areas: `/api/v1/inventory/areas` (`GET`/`POST`), `/areas/{id}` (`PATCH`/`DELETE`). No `PUT`
  (405). The `PATCH` is **JSON Patch** — only `application/json-patch+json`; a plain JSON body gets
  415. Re-parent through the UI dialog instead.
- Item activity + notes: `/api/v1/inventory/items/{id}/activity` (`GET`; params `Page`, `PageSize`
  only — stray `notesOnly`/`filter` are ignored, not rejected), `/items/{id}/activity/notes` (`POST`),
  `/items/{id}/activity/notes/{noteId}` (`PUT`/`DELETE`); bodies are `{ occurredAt?, body }` with
  `body` required. `/items/{id}/activity/subjects` no longer exists (404). `/items/{id}/notes` is not a
  route (405). Writes require Contributor; reads ride the group's ActiveUser policy.
- Consumable movements are a separate route family: `/api/v1/inventory/consumables/{id}/movements`
  (`GET`, `PageSize=25`), paged independently of the activity feed.
