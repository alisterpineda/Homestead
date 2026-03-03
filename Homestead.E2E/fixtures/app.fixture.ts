import { test as base } from '@playwright/test';
import { SetupPage } from '../pages/setup.page';
import { LoginPage } from '../pages/login.page';
import { KeycloakLoginPage } from '../pages/keycloak-login.page';

type AppFixtures = {
  setupPage: SetupPage;
  loginPage: LoginPage;
  keycloakLoginPage: KeycloakLoginPage;
};

export const test = base.extend<AppFixtures>({
  setupPage: async ({ page }, use) => {
    await use(new SetupPage(page));
  },
  loginPage: async ({ page }, use) => {
    await use(new LoginPage(page));
  },
  keycloakLoginPage: async ({ page }, use) => {
    await use(new KeycloakLoginPage(page));
  },
});

export { expect } from '@playwright/test';
