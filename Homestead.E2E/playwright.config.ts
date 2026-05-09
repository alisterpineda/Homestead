import { defineConfig, devices } from '@playwright/test';
import { HOMESTEAD_CONFIGS } from './infra/homestead.js';

const baseUrl = (name: string) => {
  const config = HOMESTEAD_CONFIGS.find((c) => c.name === name)!;
  return `http://localhost:${config.hostPort}`;
};

const OIDC_URL = baseUrl('homestead-oidc');

const DESKTOP = { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } };
const MOBILE = { ...devices['Pixel 7'] };

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
      name: 'documents-upload',
      testDir: './tests/documents/upload',
      use: { ...DESKTOP, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['auth-setup'],
    },
    {
      name: 'documents-tags',
      testDir: './tests/documents/tags',
      use: { ...DESKTOP, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['auth-setup'],
    },
    {
      name: 'documents-bulk-ops',
      testDir: './tests/documents/bulk-ops',
      use: { ...DESKTOP, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['auth-setup'],
    },
    {
      name: 'documents-trash',
      testDir: './tests/documents/trash',
      use: { ...DESKTOP, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['auth-setup'],
    },
    // --- Finances (desktop) ---
    {
      name: 'finances-accounts',
      testDir: './tests/finances/accounts',
      use: { ...DESKTOP, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['auth-setup'],
    },
    {
      name: 'finances-balance-entries',
      testDir: './tests/finances/balance-entries',
      use: { ...DESKTOP, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['auth-setup'],
    },
    {
      name: 'finances-transfers',
      testDir: './tests/finances/transfers',
      use: { ...DESKTOP, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['auth-setup'],
    },
    // --- Finances (mobile) ---
    {
      name: 'finances-accounts-mobile',
      testDir: './tests/finances/accounts',
      use: { ...MOBILE, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['finances-accounts'],
    },
    {
      name: 'finances-balance-entries-mobile',
      testDir: './tests/finances/balance-entries',
      use: { ...MOBILE, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['finances-balance-entries'],
    },
    {
      name: 'finances-transfers-mobile',
      testDir: './tests/finances/transfers',
      use: { ...MOBILE, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['finances-transfers'],
    },
    // --- Mobile documents pipeline ---
    {
      name: 'documents-upload-mobile',
      testDir: './tests/documents/upload',
      use: { ...MOBILE, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['documents-upload'],
    },
    {
      name: 'documents-tags-mobile',
      testDir: './tests/documents/tags',
      use: { ...MOBILE, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['documents-tags'],
    },
    {
      name: 'documents-bulk-ops-mobile',
      testDir: './tests/documents/bulk-ops',
      use: { ...MOBILE, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['documents-bulk-ops'],
    },
    {
      name: 'documents-trash-mobile',
      testDir: './tests/documents/trash',
      use: { ...MOBILE, baseURL: OIDC_URL, storageState: '.auth/admin.json' },
      dependencies: ['documents-trash'],
    },
  ],
});
