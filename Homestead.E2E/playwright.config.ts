import { defineConfig, devices } from '@playwright/test';
import { AUTH_STATE } from './e2e.config';
import { BASE_URL } from './e2e.env';

const DESKTOP = { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } };
const ANDROID = devices['Pixel 7'];
const IPHONE = devices['iPhone 17'];

const AS_ADMIN = { baseURL: BASE_URL, storageState: AUTH_STATE.admin };

/** Workflow specs — everything except the sign-in flows, which need a signed-out browser. */
const WORKFLOWS = { testIgnore: ['**/auth/**'], dependencies: ['setup'] };

export default defineConfig({
  testDir: './tests',
  // Every run clears its output directory first; give concurrent runs on one checkout their own.
  outputDir: process.env.E2E_OUTPUT_DIR ?? 'test-results',
  globalSetup: require.resolve('./global-setup'),
  globalTimeout: 20 * 60_000,
  timeout: 60_000,
  expect: { timeout: 10_000 },

  // Tests are independent: each seeds its own uniquely-named data, and the few that touch
  // household-wide state serialize through `lock` (see LOCKS in e2e.config.ts).
  fullyParallel: true,
  workers: process.env.CI ? 2 : 4,

  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  // Retry failures at the end of the run, one at a time, instead of immediately alongside the
  // tests that may have caused them — a retry then says something about the test, not the load.
  retryStrategy: 'isolated',
  // A test that only passes on retry fails the CI run instead of hiding behind a green check.
  failOnFlakyTests: !!process.env.CI,

  reporter: process.env.CI ? [['html', { open: 'never' }], ['github']] : [['html', { open: 'on-failure' }]],

  use: {
    baseURL: BASE_URL,
    actionTimeout: 15_000,
    trace: 'retain-on-failure-and-retries',
    screenshot: 'only-on-failure',
    // The app's service worker proxies API calls once it takes control, and requests it makes
    // bypass `page.route` — which would silently defeat stubEmptyList / stubFailure.
    serviceWorkers: 'block',
  },

  projects: [
    // Signs the seeded users in once and writes their storage state; sets household defaults.
    { name: 'setup', testMatch: /.*\.setup\.ts/, fullyParallel: false, use: DESKTOP },

    // Sign-in / sign-out flows, from a signed-out browser.
    { name: 'auth', testDir: './tests/auth', use: DESKTOP, dependencies: ['setup'] },

    { name: 'desktop', ...WORKFLOWS, use: { ...DESKTOP, ...AS_ADMIN } },
    { name: 'android', ...WORKFLOWS, use: { ...ANDROID, ...AS_ADMIN } },
    // The installed web app's main audience is iOS Safari, so phones also run on WebKit.
    { name: 'iphone', ...WORKFLOWS, use: { ...IPHONE, ...AS_ADMIN } },
  ],
});
