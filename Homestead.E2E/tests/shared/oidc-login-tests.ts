import { test, expect } from '../../fixtures/app.fixture';
import { KEYCLOAK_ADMIN, KEYCLOAK_USER, KEYCLOAK_URL_PATTERN } from '../../e2e.config';

export function defineOidcLoginTests(label: string) {
  test.describe(label, () => {
    test.describe.configure({ mode: 'serial' });

    test('admin can log in via OIDC', async ({ page, loginPage, keycloakLoginPage }) => {
      await page.goto('/');
      await page.waitForURL('**/login');

      await loginPage.oidcButton.click();
      await page.waitForURL(KEYCLOAK_URL_PATTERN);

      await keycloakLoginPage.login(KEYCLOAK_ADMIN.email, KEYCLOAK_ADMIN.password);
      await page.waitForURL('**/documents');

      await page.goto('/admin');
      await expect(page).toHaveURL(/\/admin/);
    });

    test('regular user can log in via OIDC', async ({ page, loginPage, keycloakLoginPage }) => {
      await page.goto('/');
      await page.waitForURL('**/login');

      await loginPage.oidcButton.click();
      await page.waitForURL(KEYCLOAK_URL_PATTERN);

      await keycloakLoginPage.login(KEYCLOAK_USER.email, KEYCLOAK_USER.password);
      await page.waitForURL('**/documents');

      await page.goto('/admin');
      await expect(page).toHaveURL(/\/documents/);
    });
  });
}
