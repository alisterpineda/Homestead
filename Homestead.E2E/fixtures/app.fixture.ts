import { test as base } from '@playwright/test';
import { SetupPage } from '../pages/setup.page';
import { LoginPage } from '../pages/login.page';
import { KeycloakLoginPage } from '../pages/keycloak-login.page';
import { DocumentListPage } from '../pages/document-list.page';
import { DocumentDetailPage } from '../pages/document-detail.page';
import { TagListPage } from '../pages/tag-list.page';
import { TrashListPage } from '../pages/trash-list.page';
type AppFixtures = {
  setupPage: SetupPage;
  loginPage: LoginPage;
  keycloakLoginPage: KeycloakLoginPage;
  documentListPage: DocumentListPage;
  documentDetailPage: DocumentDetailPage;
  tagListPage: TagListPage;
  trashListPage: TrashListPage;
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
  documentListPage: async ({ page }, use) => {
    await use(new DocumentListPage(page));
  },
  documentDetailPage: async ({ page }, use) => {
    await use(new DocumentDetailPage(page));
  },
  tagListPage: async ({ page }, use) => {
    await use(new TagListPage(page));
  },
  trashListPage: async ({ page }, use) => {
    await use(new TrashListPage(page));
  },
});

export { expect } from '@playwright/test';
