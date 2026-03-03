import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  reporter: process.env.CI
    ? [['html', { open: 'never' }], ['github']]
    : [['html', { open: 'on-failure' }]],
  use: {
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'setup',
      testMatch: /global\.setup\.ts/,
      teardown: 'teardown',
    },
    {
      name: 'teardown',
      testMatch: /global\.teardown\.ts/,
    },
    {
      name: 'local-only',
      testDir: './tests/local-only',
      use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:3101' },
      dependencies: ['setup'],
    },
    {
      name: 'oidc-local',
      testDir: './tests/oidc-local',
      use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:3102' },
      dependencies: ['setup'],
    },
    {
      name: 'oidc-only',
      testDir: './tests/oidc-only',
      use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:3103' },
      dependencies: ['setup'],
    },
  ],
});
