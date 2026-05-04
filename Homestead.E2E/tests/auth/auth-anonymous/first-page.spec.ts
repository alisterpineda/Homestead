import { test, expect } from '../../../fixtures/auth.fixture';

test.describe('Anonymous access (fresh database)', () => {
  test('redirects to login page', async ({ page }) => {
    await page.goto('/');
    await page.waitForURL('**/login');
  });

  test('login page renders with OIDC button only', async ({ loginPage }) => {
    await loginPage.goto();

    await expect(loginPage.heading).toBeVisible();
    await expect(loginPage.description).toBeVisible();
    await expect(loginPage.oidcButton).toBeVisible();
    await expect(loginPage.oidcButton).toHaveText(/Sign in with Keycloak/);
  });

  test('API returns displayName only', async ({ request }) => {
    const response = await request.get('/api/status');
    expect(response.ok()).toBeTruthy();

    const body = await response.json();
    expect(body).toEqual({ displayName: 'Keycloak' });
  });
});
