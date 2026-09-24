# Self-QA reference — shared (always load)

Per-module files: `documents.md`, `finances.md`, `inventory.md`, `admin.md` (same directory). Load only
the module(s) the diff touches (`Homestead.WebApp/src/app/modules/<name>/`). Changes under
`src/app/shared/` affect every module — load the module whose screen you use to exercise them.

## Shell, guards, header

- Shell is `AppShellComponent` (behind `authGuard`). Page titles come from route `data.title`.
- Header action buttons are projected via `PageHeaderService` (`<ng-template #headerActions>`); they
  clear on `NavigationStart`.
- `loginGuard` bounces an already-authed user from `/login` → `/documents`. `authGuard` sends
  unauthenticated → `/login` and inactive → `/pending-activation`.
- `adminGuard` protects the whole `/admin/*` module and `/documents/settings`. Verify those are
  hidden/denied as `user@test.com` when a change touches them.

## Login (Keycloak, OIDC-only)

- `/login` has one button whose label STARTS WITH **"Sign in with"** (suffix is "SSO" until
  `/api/status` resolves, then the IdP name — match the prefix).
- Keycloak form: textbox **Username or email**, textbox **Password**, button **Sign In**. Success
  lands on `/documents`.
- Dev users (from `Homestead.AppHost/KeycloakRealms/homestead-realm.json`, not secrets):
  `admin@test.com` / `admin123` (admin), `user@test.com` / `user123` (contributor).
- Keycloak keeps its own SSO cookie: once one user is signed in, clicking "Sign in with" silently
  re-uses that identity. **`cookie-clear` before switching users.**
- Logging out takes two confirmations (not a bug): in-app `alertdialog` "Log out?" (Cancel / Log
  out), then Keycloak's own "Do you want to log out?" page.

Recipe, from `/login` (the agent file's §2 saves the result with `state-save`):

```bash
playwright-cli -s=self-qa find "Sign in with"                          # → click that ref
playwright-cli -s=self-qa find --regex "/username|password|sign in/i"  # → fill Username, fill Password, click Sign In
playwright-cli -s=self-qa --raw eval "location.pathname"               # must be "/documents"
```

## API

- Base path `/api/v1/...` (e.g. `/api/v1/documents`, `/api/v1/documents/tags`), proxied through
  `:4200`. Exceptions: `/api/status`, `/api/auth/user-info` (a 401 on user-info before login is
  normal — it is the app probing the session).
- Filter network output with `requests --filter "/api/v1/<area>"`; `request N` for one payload.

## Cross-cutting gotchas

- `run-code` has no `setTimeout` or `URL`. To simulate a slow request, hold the `page.route` handler
  and release it later; to check a download's bytes, `page.waitForEvent('download')` + `saveAs`.

- `hlm-checkbox` aria-label: bind `[aria-label]`, NOT `[attr.aria-label]`.
- **Dark mode**: sidebar user menu → **Theme** → dialog with Light / Dark / System radios. The
  `<input type=radio>` is intercepted by its wrapping `<label>` — click the label, not the radio.
- Narrow-viewport layouts switch at `md` (48rem = 768px at default font). Below it, some pages
  instantiate a *different* component tree (tabs, drawers, quick panels) — a resize destroys one and
  builds the other, so re-`find` refs after `resize`.
