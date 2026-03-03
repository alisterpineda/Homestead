import { test as setup, expect } from '../../fixtures/app.fixture';
import { KeycloakLoginPage } from '../../pages/keycloak-login.page';
import { KEYCLOAK_ADMIN, KEYCLOAK_USER, KEYCLOAK_URL_PATTERN } from '../../e2e.config';

export function defineOidcSetup() {
  setup(
    'provision OIDC users and activate regular user',
    async ({ page, loginPage, keycloakLoginPage, request }) => {
      // 1. Log in as admin via OIDC (first user — auto-activated)
      await page.goto('/');
      await page.waitForURL('**/login');
      await loginPage.oidcButton.click();
      await page.waitForURL(KEYCLOAK_URL_PATTERN);
      await keycloakLoginPage.login(KEYCLOAK_ADMIN.email, KEYCLOAK_ADMIN.password);
      await page.waitForURL('**/documents');

      // 2. Log in as regular user via OIDC in a separate context to provision them
      const userContext = await page.context().browser()!.newContext();
      const userPage = await userContext.newPage();
      await userPage.goto(page.url().replace(/\/documents.*/, '/login'));
      await userPage.getByRole('button', { name: /Sign in with/ }).click();
      await userPage.waitForURL(KEYCLOAK_URL_PATTERN);

      // Keycloak login for regular user
      const userKeycloakLogin = new KeycloakLoginPage(userPage);
      await userKeycloakLogin.login(KEYCLOAK_USER.email, KEYCLOAK_USER.password);

      // Regular user should land on pending-activation
      await userPage.waitForURL('**/pending-activation');
      await userContext.close();

      // 3. Admin activates the regular user via API
      // First, get cookies from the admin's page context for API calls
      const cookies = await page.context().cookies();
      const cookieHeader = cookies.map((c) => `${c.name}=${c.value}`).join('; ');

      // Get users list
      const baseUrl = new URL(page.url()).origin;
      const usersResponse = await request.get(`${baseUrl}/api/v1/admin/users?Page=1&PageSize=50`, {
        headers: { Cookie: cookieHeader },
      });
      expect(usersResponse.ok()).toBeTruthy();
      const usersBody = await usersResponse.json();
      const regularUser = usersBody.items.find(
        (u: { email: string }) => u.email === KEYCLOAK_USER.email,
      );
      expect(regularUser).toBeDefined();

      // Activate the regular user
      const activateResponse = await request.put(
        `${baseUrl}/api/v1/admin/users/${regularUser.id}`,
        {
          headers: { Cookie: cookieHeader },
          data: {
            email: regularUser.email,
            displayName: regularUser.displayName,
            isAdmin: false,
            isActive: true,
          },
        },
      );
      expect(activateResponse.ok()).toBeTruthy();

      // 4. Log out admin so the login tests start fresh
      await page.goto(`${baseUrl}/api/auth/logout`, { waitUntil: 'networkidle' });
    },
  );
}
