import { test as base } from './base.fixture';
import { AccountListPage } from '../pages/finances/account-list.page';
import { AccountDetailPage } from '../pages/finances/account-detail.page';
import { CategoryListPage } from '../pages/finances/category-list.page';
import { PayeeListPage } from '../pages/finances/payee-list.page';
import { CommodityListPage } from '../pages/finances/commodity-list.page';
import { CommodityDetailPage } from '../pages/finances/commodity-detail.page';
import { ReportsPage } from '../pages/finances/reports.page';
import { GeneralSettingsPage } from '../pages/finances/general-settings.page';
import { PriceSourceListPage } from '../pages/finances/price-source-list.page';
import { PriceSourceDetailPage } from '../pages/finances/price-source-detail.page';

type FinancesFixtures = {
  accountListPage: AccountListPage;
  accountDetailPage: AccountDetailPage;
  categoryListPage: CategoryListPage;
  payeeListPage: PayeeListPage;
  commodityListPage: CommodityListPage;
  commodityDetailPage: CommodityDetailPage;
  reportsPage: ReportsPage;
  generalSettingsPage: GeneralSettingsPage;
  priceSourceListPage: PriceSourceListPage;
  priceSourceDetailPage: PriceSourceDetailPage;
};

export const test = base.extend<FinancesFixtures>({
  accountListPage: async ({ page }, use) => {
    await use(new AccountListPage(page));
  },
  accountDetailPage: async ({ page }, use) => {
    await use(new AccountDetailPage(page));
  },
  categoryListPage: async ({ page }, use) => {
    await use(new CategoryListPage(page));
  },
  payeeListPage: async ({ page }, use) => {
    await use(new PayeeListPage(page));
  },
  commodityListPage: async ({ page }, use) => {
    await use(new CommodityListPage(page));
  },
  commodityDetailPage: async ({ page }, use) => {
    await use(new CommodityDetailPage(page));
  },
  reportsPage: async ({ page }, use) => {
    await use(new ReportsPage(page));
  },
  generalSettingsPage: async ({ page }, use) => {
    await use(new GeneralSettingsPage(page));
  },
  priceSourceListPage: async ({ page }, use) => {
    await use(new PriceSourceListPage(page));
  },
  priceSourceDetailPage: async ({ page }, use) => {
    await use(new PriceSourceDetailPage(page));
  },
});

export { expect } from './base.fixture';
