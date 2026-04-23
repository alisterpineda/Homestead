---
name: self-qa
description: "Self-validate UI-affecting changes in Homestead.WebApp by driving a real browser against the running Aspire stack via the `playwright` MCP server — probe the stack, start it if needed, sign in through Keycloak, exercise the affected screens, check the browser console, and tear down if Claude started the stack. Trigger this skill before declaring any frontend task complete: whenever you have edited Angular templates, components, styles, routes, guards, services that change UI behavior, or dialog/form flows in Homestead.WebApp/, and also when the user asks you to 'check', 'verify', 'test in the browser', 'try it out', 'see if it works', or otherwise confirm a UI change end-to-end. The Homestead.E2E Playwright suite is separate regression coverage — this skill is for ephemeral in-session validation only."
---

# Self-QA with Playwright MCP

Use this skill before reporting any UI-affecting change as complete. It drives the `playwright` MCP server (registered in `.mcp.json` at the repo root) against the running Aspire stack so you can *see* the change behave correctly, instead of inferring from the diff.

This is ephemeral, in-session validation. The durable regression suite lives in `Homestead.E2E/` and is out of scope here — do not edit E2E tests or the `.auth/` storage state as part of a self-QA pass.

## When the skill applies

Run the full workflow whenever a change could affect what a user sees or does in the browser. That includes edits to:

- Angular templates, components, styles, directives under `Homestead.WebApp/src/`
- Routes, guards, resolvers, interceptors
- Services or signals that feed into a rendered screen
- Page header actions, sidebar, shell, theme

Skip the browser workflow for pure refactors with no behavioral change, backend-only changes, or doc-only changes — but say so explicitly in your summary so it's clear you didn't silently skip validation.

If the MCP server is unreachable or the stack fails to come up, do not silently move on. Report "UI validation not run: <reason>" in the final summary so the developer can decide how to proceed.

## Workflow

The four stages are: **probe → start if needed → log in → exercise → teardown if you started it**. Work through them in order.

### 1. Probe whether the stack is already running

Two curl probes — run them in parallel (single Bash message, two calls, or combined with `&&`):

```bash
curl -sSf -o /dev/null -m 2 http://localhost:4200 && echo ui-up || echo ui-down
curl -sSf -o /dev/null -m 2 http://localhost:8080/realms/homestead/.well-known/openid-configuration && echo oidc-up || echo oidc-down
```

- **Both up** → developer already has Aspire running. Skip straight to *Log in*. Do **not** launch a second copy, and do **not** tear it down at the end of the session.
- **Either down** → continue to step 2 and start the stack yourself.

### 2. Start Aspire in the background (only if the probe said down)

Launch via the Bash tool with `run_in_background: true` so you can keep working while it boots:

```bash
dotnet run --project Homestead.AppHost
```

Record mentally (or in a TaskCreate item) that *you* started it — that determines whether you tear it down at the end.

Cold-start timing: a fresh devcontainer can take several minutes because Postgres and Keycloak images get pulled via DinD. Warm starts are ~30–60 seconds.

**Wait for readiness** by re-running the two probes from step 1 at ~2-second intervals. Use a ~180-second overall timeout on a cold start and ~60 seconds when warm. If the timeout expires, read the background task's stdout/stderr for the real failure reason, report it to the developer, and stop. Never silently proceed to the browser steps against a half-started stack — the resulting failures will look like UI bugs and waste time.

Once both probes return up, the app is at `http://localhost:4200` and Keycloak is at `http://localhost:8080`.

### 3. Log in through Keycloak

Auth is OIDC-only — the browser has to complete a Keycloak login before it can see any protected screen. Drive it with the Playwright MCP tools:

1. `browser_navigate` to `http://localhost:4200`. Angular redirects to `/login`.
2. `browser_click` the **Sign in with …** button (the label ends with whatever `/api/status` reports as the OIDC display name — "SSO" initially, then usually "Keycloak" once the status call resolves; match on the "Sign in with" prefix, not the full label). The tab redirects to Keycloak on `localhost:8080`.
3. `browser_type` the dev email into the **Username or email** field: `admin@test.com`.
4. `browser_type` the dev password: `admin123`.
5. `browser_click` the Keycloak **Sign In** submit button and wait for the redirect back to `/documents`.

These credentials are **not secrets** — they are the imported dev users from `Homestead.AppHost/KeycloakRealms/homestead-realm.json` and are referenced openly in `Homestead.E2E/e2e.config.ts`. Use `user@test.com` / `user123` instead if the change needs a non-admin session (e.g., checking that an admin-only control is hidden). Never introduce other credentials into this workflow.

If login fails — for example, the SSO button doesn't render, the Keycloak page 500s, or the redirect never lands on `/documents` — stop and report the failure rather than guessing at new credentials or retrying forever.

### 4. Exercise the change and look for regressions

Walk through the change with intent, not just clicks. The goal is to catch both the bug in front of you and the class of bug it belongs to.

1. **Navigate to the affected screen(s)** and confirm the change is visible and behaves as intended — not just "pixels on the page" but the actual interaction.
2. **Exercise the golden path** for the affected feature. For a form change: open the form, fill it, submit, confirm the success state. For a list change: verify sort, filter, pagination, selection still work.
3. **Walk through at least one adjacent screen** that imports the same component or service, to catch cross-cutting regressions. If you touched a shared button directive, a dialog wrapper, or an interceptor, a single-screen check is not enough.
4. **Check the browser console** via `browser_console_messages`. Any new error, warning tied to your change, or uncaught promise rejection counts as a failure — do not report success past one.
5. **If the change touches an API call**, verify the request/response via `browser_network_requests`. A UI that "looks right" but fires the wrong payload or silently swallows a 4xx is still a regression.
6. **If the change touches an admin-only path**, do the admin-path walkthrough as `admin@test.com` *and* confirm it is correctly hidden/denied as `user@test.com` (log out via the shell, or open a fresh session).

Snapshots from `browser_snapshot` are usually more useful than screenshots for reasoning about structure — reach for screenshots when the issue is visual (spacing, color, alignment).

When you *do* take a screenshot, **always prefix the filename with `.playwright-mcp/`** (e.g., `filename: ".playwright-mcp/foo.png"`). The MCP server's `--output-dir` only governs auto-named artifacts (console dumps, snapshot YAMLs); an explicit `filename` is passed straight to Playwright's `page.screenshot({ path })` and resolves against the MCP server's CWD — the repo root — so a bare name like `foo.png` will litter the project root.

### 5. Teardown — only if you started the stack

If step 1's probe said *down* and you launched Aspire yourself, kill it at the end of the self-QA pass so the developer doesn't inherit a stranded background process:

```bash
pkill -f "Homestead.AppHost"
```

If the developer already had the stack running when you started, leave it running. Closing out a process you didn't start is a guaranteed way to lose their state.

Also close the browser context with `browser_close` before finishing.

## Reporting

Include the self-QA outcome in your final summary to the developer. Useful phrasings:

- "Self-QA via Playwright MCP: signed in as admin, verified the new tag badge renders on document detail, no console errors."
- "Self-QA partially run: stack came up, login succeeded, but the affected page 500'd — see attached error."
- "Self-QA skipped: backend-only change, no UI surface touched."
- "UI validation not run: Aspire failed to start within 180s — see background task log for the stack trace."

Be specific about *what* you verified, not just that you verified. "Tested the change" is not enough — the developer should be able to tell from your summary which screens and flows you actually exercised.
