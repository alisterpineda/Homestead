import { test, expect } from '../../fixtures/auth.fixture';
import { KEYCLOAK_ADMIN, KEYCLOAK_URL_PATTERN, KEYCLOAK_USER } from '../../e2e.config';

test.describe('OIDC sign-in', () => {
  test('the admin signs in and can open Admin', async ({ page, loginPage }) => {
    await loginPage.signIn(KEYCLOAK_ADMIN);
    await expect(page).toHaveURL(/\/documents/);

    await page.goto('/admin');
    // Admin content, not just the URL: the URL is /admin before the route guard has decided.
    await expect(page.getByText('Admin features coming soon')).toBeVisible();
    await expect(page).toHaveURL(/\/admin/);
  });

  test('a contributor signs in but is turned away from Admin', async ({ page, loginPage }) => {
    await loginPage.signIn(KEYCLOAK_USER);
    await expect(page).toHaveURL(/\/documents/);

    await page.goto('/admin');
    await expect(page).toHaveURL(/\/documents/);
  });

  test('signing out ends both the app and the Keycloak session', async ({ page, loginPage }) => {
    await loginPage.signIn(KEYCLOAK_ADMIN);
    await expect(page).toHaveURL(/\/documents/);

    // The real OIDC sign-out: cookie sign-out plus Keycloak end-session. Keycloak asks for
    // confirmation because the app sends client_id rather than id_token_hint.
    await page.goto(`/api/auth/oidc/logout?returnUrl=${encodeURIComponent('/login')}`);
    await expect(page).toHaveURL(KEYCLOAK_URL_PATTERN);
    await page.getByRole('button', { name: 'Logout' }).click();
    await expect(loginPage.heading).toBeVisible();

    // Signed out of the app: protected routes bounce to the login page.
    await page.goto('/documents');
    await expect(page).toHaveURL(/\/login(\?|$)/);

    // Signed out of Keycloak too: signing in asks for credentials again instead of passing through.
    await loginPage.oidcButton.click();
    await expect(page).toHaveURL(KEYCLOAK_URL_PATTERN);
    await expect(page.getByLabel('Username or email')).toBeVisible();
  });
});
