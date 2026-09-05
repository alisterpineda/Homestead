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

## Item status (Active / Lent / Archived)

Three statuses only — `Active`, `Lent`, `Archived` — plus a single free-text `statusNote` that
qualifies the non-Active ones (the borrower when Lent, an optional reason when Archived; always null
while Active). A consumable is never Lent, so the kinds differ only there.

- **No Status column and no status sort** on `/inventory/items`. The pill renders beside the **name**
  (name cell in the table, head row on a card), with the note as muted text after it — "Lent · to
  Dave", "Archived · Sold to Sam". An **Active row shows no pill at all**, so most rows carry no
  status chrome; archived rows stay italic-muted and only appear behind the refine popover's **Show
  inactive** toggle. The `?lent=true` deep link and the Browse "Lent out" tile are unchanged.
- **Lend / Mark returned** live in two places: the durable **quick panel's Status card** (one
  state-driven button — `Lend…` on an Active item opens the inline "Lent to" form, `Mark returned` on
  a Lent one patches straight away, `Restore` on an Archived one) and the durable detail page's `⋮`
  menu (`Lend…` opens a small alert dialog whose action stays disabled until a borrower is typed;
  `Mark returned` is immediate). There is **no** Lend in the list row's `⋮` — the quick panel is the
  list's fast surface. The quick panel's old six-way "Change" dropdown is gone.
- **Archive / Restore** stay in the row `⋮` and the detail `⋮`, both behind a confirm dialog. That
  dialog now carries an optional single-line **Note (optional)** input (placeholder "e.g. Sold to
  Sam", 256 chars) — it is sent as `/statusNote` alongside `/status`, and reset every time the dialog
  opens. Restore is immediate and clears the note server-side.
- **The edit sheets have no status select** (both kinds). A non-Active item shows one **Status note**
  field instead — labelled "Lent to" while Lent (required, blocks Save when blank) and "Note" while
  Archived (optional). Saving never emits `/status`.
- Archiving a container with live contents is still rejected: `Cannot archive a container while it
  has live contents.` — shown inline in the confirm dialog, not as a toast.
- **Selectors for driving it:** the kebab triggers are `button[aria-label="Durable actions"]` /
  `button[aria-label="Consumable actions"]` on both the list row and the detail page (there is no
  "Item actions" / "More" label). The quick panel's inline lend form confirms with **`Mark as lent`**
  (input placeholder `e.g. Dave next door`); the detail page's dialogs confirm with **`Lend`** and
  **`Archive`** — match those `exact: true`, since "Archive" also hits the menu item. The lend dialog
  input is `#lend-to` and the durable edit sheet's status-note field is `#durable-status-note`
  (both `maxlength=256`). The edit sheet's "Lent to is required…" message renders as a form-level
  error at the bottom of the sheet body, not beside the field.
- The activity feed prints the transition, not "updated status": "Alister lent this item to Dave",
  "…marked this item returned", "…archived this item (Sold to Sam)", "…restored this item". A
  note-only edit reads "updated status note". Nothing writes a second note row for a status change.

## Warranties (durable detail page only)

`app-item-warranties-section` sits on `/inventory/items/:id` for a **durable**, between
Components/Contents and Activity (heading `Warranties`). A consumable's detail page has no such
section. Load fires one `GET …/warranties` (unpaged); every mutation re-reads the list rather than
splicing, because row `status` is derived server-side.

Composer (one row, at the head of the section). **Scope every selector to
`app-item-warranties-section`** — the same page also hosts the activity composer, and unscoped
queries hit both:

| Element | Selector / accessible name |
|---|---|
| Expiry chip | `button[aria-label^="Expiry:"]` — label text `Expires` (unset, muted) / `No expiry` / a formatted date |
| Chip menu | the chip is a dropdown trigger; items are `1 year · {date}`, `2 years · {date}`, `3 years · {date}`, `5 years · {date}`, separator, `No expiry`, `Pick a date...` |
| Provider input | first `input[hlmInputGroupInput]`; AT/visible label text **`Warranty provider`** (that is what text-based finds match), placeholder `Manufacturer`, `maxlength=200` |
| Reveal notes | `button[aria-label="Add warranty note"]` (speech-bubble icon, inside the input group) — deliberately *not* "Add note", which is the activity composer's submit button |
| Notes input | second `input[hlmInputGroupInput]`, label `Warranty note`, placeholder `Note (e.g. motor only)`, `maxlength=500` — hidden until revealed |
| Submit | `button[aria-label="Add warranty"]` (send icon) — **disabled until the expiry chip is set**; provider/notes never unlock it |
| Native date input | `input[type=date]` overlaying the chip, no `max` (a lapsed date is allowed to be back-filled) |

- **Driving the expiry from a browser:** in headless Chromium `Pick a date...` calls `showPicker()`
  without error, so the native input stays `opacity-0 pointer-events-none` and there is nothing to
  click. The reliable recipe is `fill` on `app-item-warranties-section input[type=date]` — that
  updates the chip label and unlocks Add. (The chip only goes `invisible` and swaps in the native
  control when `showPicker()` is missing or refused — jsdom and older Safari, not headless Chrome.)
  The four presets and `No expiry` are ordinary menu items and can be clicked normally.
- Enter in either input submits. After a successful add the composer clears **including the chip**
  (back to `Expires`) — deliberately unlike the note composer's sticky date.
- Rows: shield icon, provider label, `· {notes}` when present, then the muted expiry line
  (`until {date}` / `expired {date}` / `no expiry`). A null provider renders as `Manufacturer`, or
  `{Brand} · manufacturer` when the item has a brand. Pills appear **only** for `Expiring soon` and
  `Expired`; an in-force warranty shows none. An expired row's provider label is muted.
- Row `⋮` is `button[aria-label="Warranty actions"]` → **Edit** / **Delete**. Edit swaps the row for
  the same composer with `Cancel` / `Save` and disables the header composer. Escape cancels the edit
  **only while focus is inside the edit composer's provider/notes input** (the keydown handler lives
  on the inputs) — same as the note composer; click `Cancel` otherwise. Delete opens an alertdialog
  titled **Delete this warranty?**; a rejection renders inline in the dialog.
- There are **no warranty fields on the create/edit durable forms or the quick panel** — the detail
  section is the only entry point.

### Warranty facet + Browse tile

- The filter popover has a single-choice **Warranty** group after the checkboxes:
  `Any` · `Active` · `Expiring soon` · `Expired` · `None`, each with a count and a check on the
  selected one. Choosing one sets the trigger label to `Filter: Warranty expiring` (the facet
  *value*, not the menu label). `Reset` returns it to `Any`. Consumables drop out of every facet but
  `Any` — warranties are durables-only.
- The group is on the shared `app-inventory-items-view`, so it appears **both** on
  `/inventory/items` and in a drilled Browse place's item table (`/inventory/browse/area/:areaId`),
  where it refines that place's rows and sets the same `Filter: Warranty expiring` label. Only the
  Browse *tile* below is root-scope-only.
- Counts respect every narrowing the list itself applies, **including "Show inactive"** — a badge
  always equals the number of rows you get by selecting it. (Regression watch: they once ignored the
  active/inactive narrowing while honouring search/area scope, so `None (44)` yielded 35 rows.)
- Deep link: `/inventory/items?warranty=expiring` (also `active`/`expired`/`none`); an unknown value
  falls back to `any`, and the param is stripped from the URL on arrival like the other seeds.
- Browse root's **Needs attention** panel gains a **Warranty expiring** tile (shield-alert icon)
  when `expiringWarrantyCount > 0`, previewing up to 5 as `{name}` + muted `until {date}`, linking
  to `?warranty=expiring`. The panel is **collapsed by default** — click the `Needs attention …`
  toggle to reveal the tile; the `N expiring` summary chip is on that collapsed trigger row.
  **Root scope only** — a drilled area/container never shows the tile.

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
- Item warranties: `/api/v1/inventory/items/{id}/warranties` (`GET` list, `POST` create → 201 +
  Location), `/items/{id}/warranties/{warrantyId}` (`PUT` full replace → 200, `DELETE` → 204).
  Bodies are `{ provider?, expiresOn?, notes? }` — **all three nullable**, so `{}` is a valid create
  (a lifetime manufacturer warranty). `expiresOn` is a bare `YYYY-MM-DD` date. Responses carry a
  derived `status` of `None|Active|Expiring|Expired`; the list is ordered by `expiresOn` ascending
  with nulls last. `POST` on a **consumable** is a 400 problem details:
  `"Warranties apply to durable items only."`. Writes require Contributor; reads ride the group's
  ActiveUser policy. Deleting a durable that has warranties succeeds (FK cascade, no delete gate —
  unlike notes).
- `GET /api/v1/inventory/items/{id}` exposes `durable.warrantyStatus`; `GET
  /api/v1/inventory/overview` carries `expiringWarrantyCount` + `expiringWarranties` (max 5, most
  urgent first). The items **list** is fed by `GET /api/v1/inventory/durables` (plus
  `/inventory/consumables`), and each durable row carries `warrantyStatus` — that is what the
  client-side Warranty facet filters on. There is **no** `/api/v1/inventory/items` list route
  (`/items/{id}` is the only route under that prefix). The warranty CRUD also shows up in the item activity feed as "added a warranty" /
  "updated a warranty (expiry)" / "removed a warranty (Bosch · until 2028-03-12)".
- Consumable movements are a separate route family: `/api/v1/inventory/consumables/{id}/movements`
  (`GET`, `PageSize=25`), paged independently of the activity feed.
