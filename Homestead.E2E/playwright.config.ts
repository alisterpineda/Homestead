import { defineConfig, devices } from '@playwright/test';
import { HOMESTEAD_CONFIGS } from './infra/homestead.js';

const baseUrl = (name: string) => {
  const config = HOMESTEAD_CONFIGS.find((c) => c.name === name)!;
  return `http://localhost:${config.hostPort}`;
};

const LOCAL_URL = baseUrl('homestead-local');
const OIDC_LOCAL_URL = baseUrl('homestead-oidc-local');
const OIDC_ONLY_URL = baseUrl('homestead-oidc-only');

export default defineConfig({
  testDir: './tests',
  globalSetup: require.resolve('./global-setup'),
  globalTimeout: 300_000,
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
      name: 'local-only',
      testDir: './tests/local-only',
      use: { ...devices['Desktop Chrome'], baseURL: LOCAL_URL },
    },
    {
      name: 'local-auth-setup',
      testMatch: /local-auth-setup\.ts/,
      use: { baseURL: LOCAL_URL },
      dependencies: ['local-only'],
    },
    {
      name: 'local-admin',
      testDir: './tests/local-admin',
      use: { ...devices['Desktop Chrome'], baseURL: LOCAL_URL },
      dependencies: ['local-auth-setup'],
    },
    {
      name: 'local-user',
      testDir: './tests/local-user',
      use: { ...devices['Desktop Chrome'], baseURL: LOCAL_URL },
      dependencies: ['local-auth-setup'],
    },
    {
      name: 'local-inactive',
      testDir: './tests/local-inactive',
      use: { ...devices['Desktop Chrome'], baseURL: LOCAL_URL },
      dependencies: ['local-auth-setup'],
    },
    {
      name: 'oidc-local',
      testDir: './tests/oidc-local',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_LOCAL_URL },
    },
    {
      name: 'oidc-local-setup',
      testDir: './tests/oidc-local-setup',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_LOCAL_URL },
      dependencies: ['oidc-local'],
    },
    {
      name: 'oidc-local-login',
      testDir: './tests/oidc-local-login',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_LOCAL_URL },
      dependencies: ['oidc-local-setup'],
    },
    {
      name: 'oidc-only',
      testDir: './tests/oidc-only',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_ONLY_URL },
    },
    {
      name: 'oidc-only-setup',
      testDir: './tests/oidc-only-setup',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_ONLY_URL },
      dependencies: ['oidc-only'],
    },
    {
      name: 'oidc-only-login',
      testDir: './tests/oidc-only-login',
      use: { ...devices['Desktop Chrome'], baseURL: OIDC_ONLY_URL },
      dependencies: ['oidc-only-setup'],
    },
  ],
});
