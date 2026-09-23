import { test as base } from './base.fixture';
import { AreaListPage } from '../pages/inventory/area-list.page';
import { BrandListPage } from '../pages/inventory/brand-list.page';
import { BrowsePage } from '../pages/inventory/browse.page';
import { InventoryTagListPage } from '../pages/inventory/inventory-tag-list.page';
import { ItemDetailPage } from '../pages/inventory/item-detail.page';
import { ItemListPage } from '../pages/inventory/item-list.page';

type InventoryFixtures = {
  areaListPage: AreaListPage;
  brandListPage: BrandListPage;
  browsePage: BrowsePage;
  inventoryTagListPage: InventoryTagListPage;
  itemDetailPage: ItemDetailPage;
  itemListPage: ItemListPage;
};

export const test = base.extend<InventoryFixtures>({
  areaListPage: async ({ page }, use) => {
    await use(new AreaListPage(page));
  },
  brandListPage: async ({ page }, use) => {
    await use(new BrandListPage(page));
  },
  browsePage: async ({ page }, use) => {
    await use(new BrowsePage(page));
  },
  inventoryTagListPage: async ({ page }, use) => {
    await use(new InventoryTagListPage(page));
  },
  itemDetailPage: async ({ page }, use) => {
    await use(new ItemDetailPage(page));
  },
  itemListPage: async ({ page }, use) => {
    await use(new ItemListPage(page));
  },
});

export { expect } from './base.fixture';
