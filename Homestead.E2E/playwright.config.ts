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
    // --- Auth bootstrap (shared across modules) ---
    {
      name: 'oidc-only',
      testDir: './tests/auth/oidc-only',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_URL },
    },
    {
      name: 'oidc-only-setup',
      testDir: './tests/auth/oidc-only-setup',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_URL },
      dependencies: ['oidc-only'],
    },
    {
      name: 'oidc-only-login',
      testDir: './tests/auth/oidc-only-login',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_URL },
      dependencies: ['oidc-only-setup'],
    },
    // --- Documents pipeline (uses admin storageState from oidc-only-setup) ---
    {
      name: 'documents-data-setup',
      testDir: './tests/documents',
      testMatch: /data-setup\.ts/,
      use: { baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['oidc-only-setup'],
    },
    {
      name: 'documents-upload',
      testDir: './tests/documents/upload',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['documents-data-setup'],
    },
    {
      name: 'documents-tags',
      testDir: './tests/documents/tags',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['documents-upload'],
    },
    {
      name: 'documents-bulk-ops',
      testDir: './tests/documents/bulk-ops',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['documents-upload'],
    },
    {
      name: 'documents-trash',
      testDir: './tests/documents/trash',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['documents-bulk-ops'],
    },
  ],
});
