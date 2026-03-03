import { test, expect } from '../../fixtures/app.fixture';

test.describe('OIDC + local auth mode (fresh database)', () => {
  test('redirects to login page', async ({ page }) => {
    await page.goto('/');
    await page.waitForURL('**/login');
  });

  test('login page renders with OIDC button and local form', async ({ loginPage }) => {
    await loginPage.goto();

    await expect(loginPage.heading).toBeVisible();
    await expect(loginPage.description).toBeVisible();
    await expect(loginPage.oidcButton).toBeVisible();
    await expect(loginPage.oidcButton).toHaveText(/Sign in with Keycloak/);
    await expect(loginPage.divider).toBeVisible();
    await expect(loginPage.emailInput).toBeVisible();
    await expect(loginPage.passwordInput).toBeVisible();
    await expect(loginPage.signInButton).toBeVisible();
  });

  test('API returns correct status', async ({ request }) => {
    const response = await request.get('/api/status');
    expect(response.ok()).toBeTruthy();

    const body = await response.json();
    expect(body.needsSetup).toBe(true);
    expect(body.oidc).toEqual({
      displayName: 'Keycloak',
      disableLocalLogin: false,
    });
  });
});
