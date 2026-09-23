import { test, expect } from '../../fixtures/auth.fixture';
import { expectNoA11yViolations } from '../../helpers/a11y';

test.describe('Anonymous access', () => {
  test('redirects to the login page', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/login(\?|$)/);
  });

  test('the login page offers OIDC sign-in only', async ({ loginPage }) => {
    await loginPage.goto();

    await expect(loginPage.heading).toBeVisible();
    await expect(loginPage.description).toBeVisible();
    await expect(loginPage.oidcButton).toHaveText(/Sign in with Keycloak/);
  });

  test('the login page has no accessibility violations', { tag: '@a11y' }, async ({ loginPage, makeAxeBuilder, isMobile }) => {
    await loginPage.goto();
    await expect(loginPage.oidcButton).toBeVisible();
    await expectNoA11yViolations(makeAxeBuilder(), { isMobile });
  });

  test('the status API exposes only the display name', async ({ request }) => {
    const response = await request.get('/api/status');
    await expect(response).toBeOK();
    expect(await response.json()).toEqual({ displayName: 'Keycloak' });
  });

  test('the API rejects unauthenticated calls', async ({ request }) => {
    const response = await request.get('/api/v1/documents?Page=1&PageSize=1&IncludeChildTags=false');
    expect(response.status()).toBe(401);
  });
});
