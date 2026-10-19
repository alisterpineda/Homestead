import { test as setup, expect } from '../../fixtures/auth.fixture';
import { AUTH_STATE, DISPLAY_CURRENCY, KEYCLOAK_ADMIN, KEYCLOAK_USER } from '../../e2e.config';
import { findCurrencyId, setDisplayCurrencyViaApi } from '../../helpers/api/finances';
import { LoginPage } from '../../pages/auth/login.page';

// Keep the saved storage states sign-in only: tests reveal finance values per tab (base.fixture).
setup.use({ revealFinances: false });

// The first user to sign in to a fresh database is auto-activated as the admin, so this must be
// the run's first sign-in — which is why every other project depends on this one.
setup('sign in the admin and set household defaults', async ({ page, loginPage }) => {
  await loginPage.signIn(KEYCLOAK_ADMIN);
  await expect(page).toHaveURL(/\/documents/);

  await page.context().storageState({ path: AUTH_STATE.admin });

  const cadId = await findCurrencyId(page.request, DISPLAY_CURRENCY);
  await setDisplayCurrencyViaApi(page.request, cadId);
});

setup('provision, activate and sign in the contributor', async ({ browser, page, loginPage }) => {
  await loginPage.signIn(KEYCLOAK_ADMIN);
  await expect(page).toHaveURL(/\/documents/);

  const userContext = await browser.newContext();
  const userPage = await userContext.newPage();
  const userLogin = new LoginPage(userPage);

  // Any later user is provisioned inactive and bounced back to the login page.
  await userLogin.signIn(KEYCLOAK_USER);
  await expect(userPage).toHaveURL(/\/login\?error=account-inactive/);

  const usersResponse = await page.request.get('/api/v1/admin/users?Page=1&PageSize=50');
  await expect(usersResponse).toBeOK();
  const { items } = (await usersResponse.json()) as {
    items: Array<{ id: string; email: string; displayName: string }>;
  };
  const user = items.find((u) => u.email === KEYCLOAK_USER.email);
  expect(user, `${KEYCLOAK_USER.email} was provisioned`).toBeDefined();

  const activate = await page.request.put(`/api/v1/admin/users/${user!.id}`, {
    data: { email: user!.email, displayName: user!.displayName, role: 'Contributor', isActive: true },
  });
  await expect(activate).toBeOK();

  // Keycloak still holds the user's session, so this sign-in goes straight back to the app.
  await userLogin.goto();
  await userLogin.oidcButton.click();
  await expect(userPage).toHaveURL(/\/documents/);

  await userContext.storageState({ path: AUTH_STATE.contributor });
  await userContext.close();
});
