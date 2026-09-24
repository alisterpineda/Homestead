# Self-QA reference — Documents (`/documents`, DocumentsLayoutComponent)

| Route | Component | Notes |
|---|---|---|
| `/documents` | DocumentListComponent | search via `?search=` (300 ms debounce, not component state); table/grid/upload/bulk-edit |
| `/documents/parties` | PartyListComponent | |
| `/documents/tags` | TagListComponent | table/badge/input/create/edit + color picker |
| `/documents/trash` | TrashListComponent | |
| `/documents/settings` | DocumentSettingsComponent | **adminGuard** |
| `/documents/:id` | DocumentDetailPageComponent | version + reprocess dialogs; tabs differ by breakpoint (below) |
| `/documents/:id/versions/:versionId` | DocumentDetailPageComponent | |

**Detail-page tabs differ by breakpoint.** Desktop: Info / Content / Metadata / Activity. Mobile
(<768px): **Preview** / Info / Content / Metadata / Activity — two separate `<hlm-tabs>` instances
under `@if (narrow())`. The mobile instance defaults to **Preview**, so a mobile run must click
Metadata explicitly.

**Download** (header button, both breakpoints) goes to `GET /api/v1/documents/{id}/file?download=true`
(`&versionId=` on an older version), served with `Content-Disposition: attachment`; it doesn't wait for
the preview. The version switcher only renders when the document has more than one version; it is
the breadcrumb's last item — an unnamed button wrapping a link that reads `Latest` (`Latest - <label>`
if the latest version is labelled) or `v<N>` / `v<N> - <label>` while an older version is selected,
opening menu items in the same form (`Latest…`, `v1`…). A `find` straight after `goto` can
come back empty before the page renders; wait for the Name field or Download button first.

## Backup section (Metadata tab, admin-only)

Read-only report of where this document stands in each documents backup mirror. Rendered after the
Version block and **hidden entirely when no `documents` pairing exists** — create one first at
`/admin/backup` (Add backup → Documents → `local`, see `admin.md`). The fetch is latched: it does not
happen until the Metadata tab has been activated once; the latch survives a breakpoint resize.

- Container `document-backup-state`; header refresh icon-button `refresh-backup-state`
- Per-source, suffixed with the backup source guid: `backup-source-{id}`, `availability-badge-{id}`
  (Unavailable / Paused), `last-run-badge-{id}`, `last-success-{id}`, `unavailable-reason-{id}`,
  `version-written-{id}`, `version-failure-{id}`, `version-not-written-{id}`, `version-rollup-{id}`
- `remote-link-{id}` is **Drive-only** — never renders on the default (local-target) stack.
- API: `GET /api/v1/admin/backup/documents/{documentId}` — 403 for a contributor, 404 for an unknown
  or trashed document.
