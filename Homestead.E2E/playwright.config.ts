import { defineConfig, devices } from '@playwright/test';
import { HOMESTEAD_CONFIGS } from './infra/homestead.js';

const baseUrl = (name: string) => {
  const config = HOMESTEAD_CONFIGS.find((c) => c.name === name)!;
  return `http://localhost:${config.hostPort}`;
};

const OIDC_URL = baseUrl('homestead-oidc');

export default defineConfig({
  testDir: './tests',
  globalSetup: require.resolve('./global-setup'),
  globalTimeout: 900_000,
  timeout: 60_000,
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  reporter: process.env.CI
    ? [['html', { open: 'never' }], ['github']]
    : [['html', { open: 'on-failure' }]],
  expect: {
    timeout: 10_000,
  },
  use: {
    actionTimeout: 15_000,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'oidc-only',
      testDir: './tests/oidc-only',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_URL },
    },
    {
      name: 'oidc-only-setup',
      testDir: './tests/oidc-only-setup',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_URL },
      dependencies: ['oidc-only'],
    },
    {
      name: 'oidc-only-login',
      testDir: './tests/oidc-only-login',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_URL },
      dependencies: ['oidc-only-setup'],
    },
    // --- Business workflow tests (use admin storageState from oidc-only-setup) ---
    {
      name: 'data-setup',
      testMatch: /data-setup\.ts/,
      use: { baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['oidc-only-setup'],
    },
    {
      name: 'upload',
      testDir: './tests/upload',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['data-setup'],
    },
    {
      name: 'tags',
      testDir: './tests/tags',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['upload'],
    },
    {
      name: 'bulk-ops',
      testDir: './tests/bulk-ops',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['upload'],
    },
    {
      name: 'trash',
      testDir: './tests/trash',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['bulk-ops'],
    },
  ],
});
