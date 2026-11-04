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

## AI suggestions (Info tab)

Pending AI suggestions render inline where each value is shown; an "AI suggestions (n)" bar heads the
Info tab whenever any suggestion is pending (its Approve all / Decline all buttons need two or more).

- **Name, Document Date, Expiry Date** each show an "AI Suggested" badge in the field label and ✓/✗
  buttons inside the input — `aria-label` `Approve name suggestion` / `Decline name suggestion`,
  `Approve document date suggestion` / `Decline document date suggestion`, `Approve expiry date
  suggestion` / `Decline expiry date suggestion`. On a date the ✓/✗ replace the picker's `Clear date`
  button (`Open calendar` stays).
- Hovering the input shows `Original: …` (e.g. `Original: Jan 1, 2025`, or `Original: (none)` when
  the field was empty). It opens on hover or keyboard focus of the input (like Name's). The tooltip lives in
  a CDK overlay that `find` doesn't see; read it after hovering with
  `--raw eval "[...document.querySelectorAll('.cdk-overlay-container ng-component')].map(e=>e.textContent.trim())"`.
- Decline restores the original (an empty original clears the date); approve keeps the value and
  drops the badge. A manual date edit + Save auto-declines that date's suggestion.
- The Classify dialog (header "More actions" ⋮ menu → Classify) has five checkboxes, all ticked by default: Tags, Parties,
  Name, Document date, Expiry date. The choice persists in `localStorage`
  (`homestead-classify-fields`).
- **Getting a pending suggestion:** run Classify with AI configured (`/admin/ai` reads Configured),
  or insert a row directly and set the field to match, e.g.
  `INSERT INTO documents.document_suggestions ("Id","DocumentId","Field","Status","SuggestedValue","OriginalValue","IsApplied","CreatedAt") VALUES (gen_random_uuid(), '<doc id>', 'DocumentDate', 'Pending', '2025-03-15', NULL, true, now());`
  plus `UPDATE documents.documents SET "DocumentDate" = '2025-03-15' WHERE "Id" = '<doc id>';`.
  Suggestion `Field` values are strings (`Tags`, `Name`, `Parties`, `DocumentDate`, `ExpiryDate`).
  Run SQL with `docker exec <pg> sh -c 'PGPASSWORD=$POSTGRES_PASSWORD psql -U postgres -h localhost -d postgresdb -c "..."'`.
- Unsaved detail-page edits are restored from `sessionStorage` across reloads: a reload with pending edits
  raises a `beforeunload` prompt (`dialog-accept`), and a suggestion seeded under an old draft shows the
  draft's value under the badge.
- **Content tab: the Extracted Text textarea is `readonly` until unlocked.** Writers get an **Edit text**
  button beside the label (viewers get none). When the text has engine provenance ("Extracted by …"), the
  click opens the alertdialog **"Edit extracted text?"**; its **Edit text** action unlocks (button becomes
  **Cancel edit**, a "Saving will mark this text as manually edited." note appears). Text already edited
  by hand (two lines: "Edited by … on …" then "Based on … extraction from …") or with no provenance unlocks
  with no dialog. The page-level Save sends the text; after the re-fetch the field relocks and the first
  provenance line reads "Edited by <display name>". ⋮ → Reprocess Text opens a "Reprocess Text Extraction"
  dialog (engine select) and, once the job completes, restores "Extracted by" in place.
