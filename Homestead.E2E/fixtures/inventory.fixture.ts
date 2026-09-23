import { test as base } from '@playwright/test';
import { AreaListPage } from '../pages/inventory/area-list.page';
import { BrowsePage } from '../pages/inventory/browse.page';
import { ItemDetailPage } from '../pages/inventory/item-detail.page';
import { ItemListPage } from '../pages/inventory/item-list.page';

type InventoryFixtures = {
  areaListPage: AreaListPage;
  browsePage: BrowsePage;
  itemDetailPage: ItemDetailPage;
  itemListPage: ItemListPage;
};

export const test = base.extend<InventoryFixtures>({
  areaListPage: async ({ page }, use) => {
    await use(new AreaListPage(page));
  },
  browsePage: async ({ page }, use) => {
    await use(new BrowsePage(page));
  },
  itemDetailPage: async ({ page }, use) => {
    await use(new ItemDetailPage(page));
  },
  itemListPage: async ({ page }, use) => {
    await use(new ItemListPage(page));
  },
});

export { expect } from '@playwright/test';
