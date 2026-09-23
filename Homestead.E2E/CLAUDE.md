# CLAUDE.md — Homestead.E2E

Playwright end-to-end tests. Global setup builds the app image and starts Postgres, Keycloak and the app with Testcontainers on free host ports (`e2e.env.ts`), so several runs can share a host.

## Commands

```bash
npm run e2e                                   # everything: setup → auth + desktop + android + iphone
npx playwright test tests/finances            # one module, all device projects
npx playwright test tests/finances --project=desktop
npm run lint && npm run typecheck             # both must be clean (CI runs them)
SKIP_BUILD=1 npx playwright test ...          # reuse the already-built homestead:e2e image
E2E_IMAGE=homestead:e2e-foo npx playwright test ...   # build/run under another tag
E2E_OUTPUT_DIR=test-results/foo PLAYWRIGHT_HTML_OUTPUT_DIR=playwright-report-foo npx playwright test ... # own artifacts + report — needed when runs overlap
```

Concurrent runs on one checkout are fine (ports and sign-in state are per run), but each run wipes its output directory on start and the HTML reporter rewrites `playwright-report/` when it finishes. Give overlapping runs their own `E2E_OUTPUT_DIR` and `PLAYWRIGHT_HTML_OUTPUT_DIR` (or `--reporter=json`), or they delete each other's traces and reports.

Don't pipe runs through `tail`/`head` (see the root CLAUDE.md). To read failures compactly, use `--reporter=json` with `PLAYWRIGHT_JSON_OUTPUT_NAME=<file>` and read the errors out of that file.

## Projects

| Project | What | Signed in as |
|---|---|---|
| `setup` | `tests/setup/*.setup.ts`: signs the admin in first (the first user becomes admin), activates + signs in the contributor, sets the household display currency to CAD | — |
| `auth` | `tests/auth/**`: sign-in/out flows from a signed-out browser | nobody |
| `desktop` / `android` / `iphone` | every other spec, at 1440×900 Chrome, Pixel 7 Chrome and iPhone 17 WebKit | admin (`AUTH_STATE.admin`, per run under `.auth/run-<port>/`) |

A spec that needs the contributor uses `test.use({ storageState: AUTH_STATE.contributor })`. `adminRequest` (from `fixtures/base.fixture.ts`) is an admin API client whatever the page is signed in as.

## Rules for specs

- **Tests are independent and run fully in parallel against one shared database**, on three device projects at once. Never depend on another test's leftovers, never use `mode: 'serial'`, and never wipe a module's data.
- **Isolate by name.** Every test gets a `uid` fixture (6 hex chars). Put it in every name the test creates (`` `Chequing ${uid}` ``, `` `finance-${uid}` ``), then narrow lists by searching for it before asserting on them. Lists are shared and grow during a run.
- **Seed through the API, exercise through the UI.** Arrange data with `helpers/api/<module>.ts` (`request` is the admin), and drive only the behaviour under test through the page. Assert results in the UI *and*, where the UI can't show it, through the API.
- **Uploads need unique bytes.** The backend rejects duplicate content. Build files with `helpers/files.ts` (`makePdf`, `makePng`, `copyRepoPdf`); never upload a fixture file as-is.
- **Empty states** come from `stubEmptyList(page, urlRegex)` (`helpers/network.ts`), not from an empty database. Use `stubFailure` for error-path tests.
- **Household-wide state** that can't be split by name (the trash, module settings) goes in a `test.describe(..., { lock: LOCKS.x })`. Add the lock name to `LOCKS` in `e2e.config.ts`. Keep locked groups small, because they serialize.
- **Every test asserts its outcome.** Awaiting a response is not an assertion. Check the resulting UI state, and use `expectOk(response)` / `await expect(apiResponse).toBeOK()` for status codes.
- No conditionals in tests, no `waitForTimeout`, no `force: true`, no `networkidle` (lint enforces these). Prefer web-first assertions (`await expect(locator)...`), `expect.poll` for API state and `expect(...).toPass()` to retry an action.

## Locators and page objects

- Priority: `getByRole` with an accessible name → `getByLabel`/`getByPlaceholder`/`getByText` → `getByTestId` (`data-testid`, already used in the app) → component-scoped CSS (`app-foo`) as a last resort. Never select on Tailwind classes or Spartan internals (`hlm-*`, `[data-slot]`, `[hlmPopoverContent]` is the one tolerated exception, because popovers have no role).
- If an element has no accessible name, fix the app (add an `aria-label`). That's an accessibility bug, not a test problem.
- Spartan dialogs and sheets: `page.getByRole('dialog', { name })`. The dialog role sits on a zero-size CDK container, so assert visibility or hiddenness on its **heading**, never on the dialog locator.
- One page object per route in `pages/<module>/`, exposed as a fixture in `fixtures/<module>.fixture.ts`. Decorate multi-step actions with `@step()` (`helpers/step.ts`), so reports show them as one step. Page objects may branch on viewport (`isMobileViewport`); tests may not.
- Phone layouts: modal sheets/drawers hide `<main>` from the accessibility tree while open, so close them before asserting on the page.
