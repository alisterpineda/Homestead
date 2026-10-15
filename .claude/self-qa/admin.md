# Self-QA reference — Admin (`/admin`, **adminGuard on the whole module**)

Sidebar groups: **Manage** (Users / Groups / Jobs / Schedules), **Configuration** (System Settings /
AI / Backup).

| Route | Component |
|---|---|
| `/admin` | AdminIndexComponent |
| `/admin/users` | UserListComponent (create/edit/password dialogs) |
| `/admin/groups` | GroupListComponent |
| `/admin/groups/:id` | GroupDetailPageComponent (member dialogs) |
| `/admin/jobs` | JobListComponent |
| `/admin/schedules` | ScheduleListComponent |
| `/admin/settings` | SystemSettingsComponent — runtime log-level controls; `GET`/`PATCH /api/v1/admin/settings/system` |
| `/admin/ai` | AiStatusComponent — read-only status of providers under `Ai:Providers` |
| `/admin/backup` | BackupTargetListComponent — below |

## AI (`/admin/ai`)

- **No providers in the default stack.** Define them in AppHost user-secrets
  (`Ai:Providers:{key}:Type|Endpoint|Model|ApiKey`, `Ai:DefaultProvider`), then do a full
  `aspire stop` + `aspire start` to pick them up. Record the `Ai:*` secrets first and restore them after.
  A dummy ApiKey is enough to render a configured card; don't press **Test connection** with one.
- **No test ids:** match by text.
- Card per provider: type label (`OpenAI-compatible` / `Azure AI Foundry` / `Anthropic`), "Default" and
  "Configured" / "Not configured" badges, then Endpoint / Model / API key / Max input rows. A null endpoint
  (Anthropic without an override) reads a muted **"SDK default"**.
- Conditional states: the empty state (no providers; names `Ai:Providers` / `Ai:DefaultProvider`); the
  red "No default provider is selected" warning (two or more providers and no `Ai:DefaultProvider`); and
  an unavailable provider's reason in red (e.g. "ApiKey is required."), with its Test connection disabled.

## Backup (`/admin/backup`)

> **Drive targets are not reachable in the default stack.** `Homestead.AppHost` hard-codes exactly one
> target — `local`, at `.homestead-data/backup` (`AppHost.cs:31-32`). A Drive target needs AppHost
> user-secrets (`Backup:Targets:{key}:Kind|ClientId|ClientSecret`) **and** a real Google Cloud OAuth
> client. Everything marked *Drive-only* exists in the component and cannot be exercised — do not
> spend a cycle trying.

One card per `Backup:Targets:{key}`, each nesting zero or more **source rows** (a `backup.sources` row
pairing a registered source kind with that target; `documents` is the only kind today). Each card
renders a **location line** under its title (destination path for local; Drive root folder *as
created* for Drive). Card level: destination Enabled toggle, Add backup dialog (disabled once every
kind is paired). Source level: own Enabled toggle ("Paused" badge when off, "Paused — destination
disabled" when the card's toggle is the one off), Back up now / Dry run / schedule add-edit-remove /
delete. The list also includes any *stale* target key surviving only on a `backup.sources` row
(`GetBackupTargetsHandler.cs:108-125`), so a card can exist with no matching config.

**Reachable in the dev stack** (the `local` target):

- Card: `backup-target-{key}`, `availability-badge`, `enabled-toggle-{key}`, `add-backup-{key}`,
  `no-sources-{key}`
- Per-source, suffixed with the source guid: `backup-source-{id}`, `source-enabled-toggle-{id}`,
  `paused-badge-{id}`, `source-unavailable-{id}`, `back-up-now-{id}`, `dry-run-{id}`,
  `remove-schedule-{id}`, `schedule-note-{id}`, `delete-source-{id}`, `confirm-delete-source-{id}`,
  `last-run-badge-{id}`, `last-success-{id}`, `last-run-summary-{id}`, `last-run-error-{id}`,
  `out-of-space-{id}`, `backed-up-count-{id}`, `prune-blocked-banner-{id}`, `prune-anyway-{id}`,
  `dismiss-prune-{id}`
- Add-backup dialog (`add-backup-dialog.component.ts`): trigger `add-backup-{key}`, then
  `source-kind-trigger` (the kind `hlm-select`) and `add-backup-error`

**Conditional** — in the component, absent on a healthy local stack:

- `no-targets` (page empty state; never renders while AppHost defines `local`)
- `configuration-error` + the "Unavailable" `availability-badge` (a valid local path is Available)
- `remove-schedule-{id}` / `schedule-note-{id}` — only once a schedule exists
- `out-of-space-{id}`, `last-run-error-{id}`, `prune-blocked-banner-{id}` / `prune-anyway-{id}` /
  `dismiss-prune-{id}` — only after a run that produced that condition

**Drive-only — NOT reachable:**

- Card: `connection-{key}` (renders for any Drive target, absent for local), `connected-as-{key}`,
  `free-space-{key}`, `disconnect-{key}`, `confirm-disconnect-{key}`, `auth-failed-banner-{key}`
- Location: `location-link-{key}` (anchor variant, only with a browsable URL) and
  `location-notice-{key}` (config-vs-created skew advisory). A local target reports neither.
- Connect dialog (`connect-google-dialog.component.ts`): `connect-google-{key}`, `connect-user-code`,
  `connect-verification-url`, `connect-copy-code`, `connect-waiting`, `connect-mismatch`,
  `connect-confirm-switch`, `connect-retry`, `connect-error`, `connect-denied`, `connect-expired`

Selector traps:

- **The location line has no test id when there is no URL.** With no `locationUrl` the text is a bare
  `<p class="font-mono break-all">` in the card header. Match by text or by the card.
- **`location-link-{key}` is `target="_blank"` to Google Drive.** Do not click it.
- **`backup-schedule-dialog.component.ts` has no test ids.** Select by role/text: button "Add
  schedule" / "Edit schedule", dialog title of the same name, cron input `#backup-cron` (placeholder
  `0 3 * * *`), an "Enabled" checkbox, Cancel / submit in the footer.

### Backup API (all admin-only; runs and schedules are **source**-scoped)

- `GET /api/v1/admin/backup/targets` — returns **`items`** (the cards) and **`sourceKinds`** (the
  Add-backup catalog). Not `targets`.
- `PATCH /targets/{key}` — destination enable only
- `POST /sources`; `PATCH /sources/{id}` (enable + `acknowledgePruneBlock`); `DELETE /sources/{id}`
  (optional `?cancelRun=true`)
- `POST /sources/{id}/runs`; `PUT`/`DELETE /sources/{id}/schedule`
- `GET /documents/{documentId}` — the per-document section on the document detail page (`documents.md`)
- Drive-only, not reachable: `POST /targets/{key}/connect`, `/connect/poll`, `/connect/confirm`,
  `/validate`; `DELETE /targets/{key}/connection`
