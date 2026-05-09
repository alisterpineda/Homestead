import { test as base } from '@playwright/test';
import { AccountListPage } from '../pages/finances/account-list.page';
import { AccountDetailPage } from '../pages/finances/account-detail.page';

type FinancesFixtures = {
  accountListPage: AccountListPage;
  accountDetailPage: AccountDetailPage;
};

export const test = base.extend<FinancesFixtures>({
  accountListPage: async ({ page }, use) => {
    await use(new AccountListPage(page));
  },
  accountDetailPage: async ({ page }, use) => {
    await use(new AccountDetailPage(page));
  },
});

export { expect } from '@playwright/test';
