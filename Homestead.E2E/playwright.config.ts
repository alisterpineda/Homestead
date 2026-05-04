import { defineConfig, devices } from '@playwright/test';
import { HOMESTEAD_CONFIGS } from './infra/homestead.js';

const baseUrl = (name: string) => {
  const config = HOMESTEAD_CONFIGS.find((c) => c.name === name)!;
  return `http://localhost:${config.hostPort}`;
};

const OIDC_URL = baseUrl('homestead-oidc');

const DESKTOP = { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } };

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
      name: 'auth-anonymous',
      testDir: './tests/auth/auth-anonymous',
      use: { ...DESKTOP, baseURL: OIDC_URL },
    },
    {
      name: 'auth-setup',
      testDir: './tests/auth/auth-setup',
      use: { ...DESKTOP, baseURL: OIDC_URL },
      dependencies: ['auth-anonymous'],
    },
    {
      name: 'auth-login',
      testDir: './tests/auth/auth-login',
      use: { ...DESKTOP, baseURL: OIDC_URL },
      dependencies: ['auth-setup'],
    },
    // --- Documents pipeline (uses admin storageState from auth-setup) ---
    {
      name: 'documents-data-setup',
      testDir: './tests/documents',
      testMatch: /data-setup\.ts/,
      use: { baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['auth-setup'],
    },
    {
      name: 'documents-upload',
      testDir: './tests/documents/upload',
      use: { ...DESKTOP, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['documents-data-setup'],
    },
    {
      name: 'documents-tags',
      testDir: './tests/documents/tags',
      use: { ...DESKTOP, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['documents-data-setup'],
    },
    {
      name: 'documents-bulk-ops',
      testDir: './tests/documents/bulk-ops',
      use: { ...DESKTOP, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['documents-data-setup'],
    },
    {
      name: 'documents-trash',
      testDir: './tests/documents/trash',
      use: { ...DESKTOP, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['documents-data-setup'],
    },
  ],
});
