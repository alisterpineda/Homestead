import { test as base } from './base.fixture';
import { LoginPage } from '../pages/auth/login.page';
import { KeycloakLoginPage } from '../pages/auth/keycloak-login.page';

type AuthFixtures = {
  loginPage: LoginPage;
  keycloakLoginPage: KeycloakLoginPage;
};

export const test = base.extend<AuthFixtures>({
  loginPage: async ({ page }, use) => {
    await use(new LoginPage(page));
  },
  keycloakLoginPage: async ({ page }, use) => {
    await use(new KeycloakLoginPage(page));
  },
});

export { expect } from './base.fixture';
