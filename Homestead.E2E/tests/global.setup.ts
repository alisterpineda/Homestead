import { test as setup, expect } from '@playwright/test';

setup('wait for all services to be healthy', async ({ request }) => {
  const services = [
    { name: 'local-only', url: 'http://localhost:3101/health' },
    { name: 'oidc-local', url: 'http://localhost:3102/health' },
    { name: 'oidc-only', url: 'http://localhost:3103/health' },
  ];

  for (const service of services) {
    await expect(async () => {
      const response = await request.get(service.url);
      expect(response.ok()).toBeTruthy();
    }).toPass({ timeout: 60_000, intervals: [2_000] });
  }
});
