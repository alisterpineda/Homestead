import { test as setup, expect } from '../../../fixtures/auth.fixture';
import { KeycloakLoginPage } from '../../../pages/auth/keycloak-login.page';
import { KEYCLOAK_ADMIN, KEYCLOAK_USER, KEYCLOAK_URL_PATTERN, AUTH_STATE } from '../../../e2e.config';

setup(
  'provision OIDC users and activate regular user',
  async ({ page, loginPage, keycloakLoginPage, request }) => {
    // 1. Log in as admin via OIDC (first user — auto-activated)
    await page.goto('/');
    await page.waitForURL(/\/login(\?|$)/);
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

    // Regular user should be redirected back to login with error
    await userPage.waitForURL('**/login?error=account-inactive');
    await userContext.close();

    // 3. Admin activates the regular user via API
    const cookies = await page.context().cookies();
    const cookieHeader = cookies.map((c) => `${c.name}=${c.value}`).join('; ');
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

    const activateResponse = await request.put(
      `${baseUrl}/api/v1/admin/users/${regularUser.id}`,
      {
        headers: { Cookie: cookieHeader },
        data: {
          email: regularUser.email,
          displayName: regularUser.displayName,
          role: 'Contributor',
          isActive: true,
        },
      },
    );
    expect(activateResponse.ok()).toBeTruthy();

    // 4. Disable the finances privacy mask so existing balance assertions read
    //    unmasked values. The default-on mask is correct for real users but
    //    breaks tests that grep for numeric balance text. A revealed-at anchor
    //    is required too — FinancesPrivacyAutoMaskService treats a missing or
    //    >5-min-old anchor as stale and re-masks on cold start. Stamping a
    //    far-future timestamp keeps every dependent test fresh regardless of
    //    how long the overall suite takes.
    await page.evaluate(() => {
      const FAR_FUTURE = Date.now() + 365 * 24 * 60 * 60 * 1000;
      localStorage.setItem('homestead-finances-privacy-masked', 'false');
      localStorage.setItem(
        'homestead-finances-privacy-revealed-at',
        JSON.stringify(FAR_FUTURE),
      );
    });

    // 5. Persist the admin's authenticated browser state for workflow tests
    await page.context().storageState({ path: AUTH_STATE.adminPath });

    // 6. Log out so the login tests start from an unauthenticated state.
    //    This is the real OIDC sign-out endpoint (cookie sign-out + Keycloak
    //    end-session). Keycloak asks for confirmation because the app sends
    //    client_id rather than id_token_hint; confirming lands on /login.
    await page.goto(
      `${baseUrl}/api/auth/oidc/logout?returnUrl=${encodeURIComponent(`${baseUrl}/login`)}`,
    );
    await page.waitForURL(KEYCLOAK_URL_PATTERN);
    await page.getByRole('button', { name: 'Logout' }).click();
    await page.waitForURL(/\/login(\?|$)/);
  },
);
