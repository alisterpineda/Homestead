# Table UX — Reference

Cross-cutting design reference for data tables in Homestead: toolbar layout, filter UX, selection state, responsive behaviour. Consult this before designing, implementing, or reviewing any list/table screen — Documents list, Accounts list, Transactions list, Tags, Parties, Admin tables, etc. The rules here override module-level defaults unless a module reference explicitly says otherwise.

This is product/UX design — *what* tables should look like and behave like. Implementation primitives (`HlmPopover`, `HlmSheet`, signal patterns) live in `Homestead.WebApp/CLAUDE.md`.

## Toolbar anatomy

The toolbar splits into **leading** and **trailing** zones with a semantic divide:

- **Leading edge — modifiers.** Controls that change *what subset of data is visible*: search, filter, sort, column visibility, view mode. Most prominent / broadest control sits leftmost; prominence radiates outward toward the edge.
- **Trailing edge — actions.** Controls that *change state*: export, overflow menu, and the primary CTA. The primary CTA sits at the absolute rightmost position; secondary actions tuck just inside it.

### Why this split

- LTR reading flow: users scope the view (left) before committing to actions (right).
- Form analogy: `Submit` lives on the right. The toolbar's primary action plays the same role.
- A filled CTA in the corner is the highest-contrast element on the row; corner-anchoring it balances the table's content weight on the left.

### Convention choice

Classic Material puts the overflow `⋯` at the absolute right and the primary action inside it. Modern SaaS (Linear, Notion, Airtable, Asana, Jira, Monday) flips this and places the high-emphasis CTA at the absolute right. **Homestead follows the SaaS pattern.**

### State variants

| State | Toolbar |
|---|---|
| Default | Leading: `Search`, `Filter`, `Sort`, `Columns`, `View`. Trailing: `Export` (or other secondary action), `⋯`, primary CTA (e.g. `+ Upload`, `+ New Account`). |
| Empty | Toolbar persists; modifiers feel inert (faded). The empty canvas hoists a duplicate primary CTA centred for first-time users. Repeat users still find the CTA where they expect it. |
| Selection | Toolbar swaps. Bulk actions (`Edit tags`, `Archive`, `Delete`) anchor to the leading edge next to an `N selected` counter. `Clear` sits trailing as the escape hatch. Destructive actions get red text rather than red buttons — colour signals intent without forcing confirmation emphasis. |

## Responsive behaviour

Desktop → mobile transformations:

- Search input → magnifying-glass icon (tap to expand).
- Filter button → funnel icon with count badge.
- Sort / Columns / View → collapse into the overflow `⋯` menu.
- Primary CTA → either keeps its label or compresses to `+`.
- Page title appears in the leading slot on mobile (absent on desktop, where the app shell already renders it).

The zone rules still hold; controls just collapse to icons through progressive disclosure.

The breakpoint is **768px**, matching the sidebar primitive (`useIsMobile()` equivalent — see `UserPreferencesService` / sidebar conventions in the WebApp).

## Filter pattern — unified popover

**One panel, two containers.** The same filter-panel content renders inside `HlmPopover` on desktop and `HlmSheet` (from bottom) on mobile. Container chrome differs (anchored, no backdrop vs. slide-up with backdrop); the body content is identical.

### Why this over a chip row

Chip rows have great at-a-glance scanability but introduce layout shift when filters change, eat vertical space, and produce divergent mobile/desktop UX. For Homestead's data tables — where a household member filters occasionally to locate specific records — the unified popover wins on simplicity and maintenance cost.

The tradeoff is scanability: `Filter (3)` doesn't tell you *what* is filtered. Two mitigations:

1. **Trigger transformation on desktop.** When filters are active, the button reads `Filter: Tags, Date +1` instead of `Filter (3)`. Costs ~80px of toolbar width, gains at-a-glance dimension list.
2. **Mobile keeps icon-only with count badge.** Horizontal space is too precious to spell out dimensions on small viewports.

### Panel anatomy

- **Header**: `Filters` left, `Reset` right.
- **Body**: one row per filter dimension, each with its inline editor.
  - Multi-select dimensions (Tags, Parties, Account kinds) — searchable list with checkboxes.
  - Single-select dimensions (Status, Currency) — pill toggles or radio group.
  - Date dimensions — preset buttons (`Last 30 days`, `This month`, `Year to date`, …) plus custom range.
- **`+ Add filter`** (dashed, secondary) at the bottom for adding new dimensions to the panel.

The mobile sheet's chrome differs only in container: a small visual handle/title row above the header is acceptable but not required (Spartan does not ship a Vaul-style drag handle).

### Behaviour

- **Auto-apply.** Every toggle/change updates the table behind the panel live. No `Apply` button.
- **`Reset`** (header) clears all filters at once.
- **Click-outside** (desktop) or **swipe-down / backdrop tap** (mobile) dismisses.
- **`Escape`** closes on desktop and returns focus to the trigger.
- **No close button** on desktop popover (matches Spartan/shadcn convention); mobile sheet dismisses via backdrop tap or its built-in close affordance.

### Implementation shape (Angular + Spartan.NG)

Pseudo-code — exact APIs in `Homestead.WebApp/src/app/shared/components/spartan/`:

```typescript
@Component({
  template: `
    @if (!isMobile()) {
      <div hlmPopover>
        <button hlmPopoverTrigger ...><!-- filter button --></button>
        <div hlmPopoverContent *hlmPopoverPortal align="start" class="w-80">
          <ng-container *ngTemplateOutlet="filterPanel" />
        </div>
      </div>
    } @else {
      <hlm-sheet side="bottom">
        <button hlmSheetTrigger ...><!-- filter button --></button>
        <hlm-sheet-content>
          <ng-container *ngTemplateOutlet="filterPanel" />
        </hlm-sheet-content>
      </hlm-sheet>
    }
    <ng-template #filterPanel><!-- shared body --></ng-template>
  `,
})
```

The shared `filterPanel` template means there is exactly one source of truth for the filter body across viewports.

## Adopted patterns (already in place across the app)

These are not future considerations — apply them in any new table.

- **URL-encoded filter state.** Active filters and sort live in query params (`?tagIds=…&sortBy=…&sortDir=…`). Enables shareable filtered links, refresh-without-loss, and browser back/forward through filter history. .NET model binding handles this cleanly on the backend; on the frontend, read via `route.queryParamMap` and write via `router.navigate({ queryParamsHandling: 'merge' })`.
- **Debounced search.** Search input is a writable signal; URL update is debounced 300ms. Don't fire on every keystroke.
- **Persistent column visibility.** Use `persistedSignal()` (via `UserPreferencesService` for cross-cutting prefs) so column choices survive sessions.

## Future considerations

- **Saved views.** Once a user lands on the same filter combination repeatedly, they want to save it (Linear Views, Airtable Views). Design any new toolbar with a slot for this — likely a tab strip above the toolbar — so adding it later doesn't force a layout rewrite. The existing per-list-state services that auto-restore last-used params (e.g. `DocumentListStateService`) are the seam this would extend.
- **Filter presets.** Common queries become one-tap segmented controls above the toolbar (`All / Active / Archived`, `All / Cash / Investments`). Distinct from saved views — presets are app-level defaults, views are user-level.
- **Chip-row pattern as an alternative.** Reserved for screens where filters change constantly and need permanent visibility (none in v1). If revisited: chip anatomy is `Property: Value ×`, multi-value collapses to count (`Tags: 3 ×`), always include the property name, `Clear all` at end of row, overflow handled by truncate-with-`+N more` popover (not wrap or scroll).

## Quick reference

| Decision | Choice |
|---|---|
| Toolbar split | Leading = modifiers, trailing = actions |
| Within-edge order | Prominence radiates outward toward the edge |
| Primary CTA position | Absolute rightmost |
| Empty state | Toolbar persists (modifiers faded); CTA also duplicated in canvas |
| Selection state | Bulk actions leading, `Clear` trailing |
| Filter UI (desktop) | `HlmPopover` anchored to trigger |
| Filter UI (mobile) | `HlmSheet` from bottom |
| Filter apply | Auto-apply on change |
| Filter active indicator | Trigger label transforms (desktop) / count badge (mobile) |
| Filter state location | URL query params |
| Mobile breakpoint | 768px (matches sidebar primitive) |
