import { test, expect } from '../../../fixtures/inventory.fixture';
import { TEST_DATA } from '../../../e2e.config';
import {
  createAreaViaApi,
  createConsumableViaApi,
  getConsumableViaApi,
  resetInventoryViaApi,
  type ConsumableHandle,
} from '../../../helpers/api/inventory';

const { areas, consumables, durables } = TEST_DATA.inventory;

test.describe.configure({ mode: 'serial' });

test.describe('Inventory — consumable stock', () => {
  let paperTowelsId: string;
  let batteries: ConsumableHandle;

  test.beforeAll(async ({ request }) => {
    await resetInventoryViaApi(request);
    const pantry = await createAreaViaApi(request, areas.pantry);
    batteries = await createConsumableViaApi(request, {
      name: consumables.batteries,
      unit: 'batteries',
      initialQuantity: 4,
      initialAreaId: pantry.id,
    });
  });

  test.afterAll(async ({ request }) => {
    await resetInventoryViaApi(request);
  });

  test('creates a consumable with starting stock and a restock threshold', async ({
    itemListPage,
    itemDetailPage,
  }) => {
    await itemListPage.goto();
    await itemListPage.createItemDialog.open(itemListPage.newItemButton.first());
    paperTowelsId = await itemListPage.createItemDialog.create({
      kind: 'Consumable',
      name: consumables.paperTowels,
      location: areas.pantry,
      startingStock: '6',
      unit: 'rolls',
      restockThreshold: '2',
    });

    await itemDetailPage.gotoById(paperTowelsId);
    await expect(itemDetailPage.nameHeading()).toHaveText(consumables.paperTowels);
    await expect(itemDetailPage.onHandText()).toContainText('6 rolls on hand');
    await expect(itemDetailPage.lowBadge()).toHaveCount(0);
    await expect(itemDetailPage.stockRow(areas.pantry)).toContainText('6 rolls');
    // The starting stock is a real inflow on the ledger, not a bare count.
    await expect(itemDetailPage.movementRows()).toHaveCount(1);
  });

  test('using stock down to the threshold flags it as low', async ({ itemDetailPage }) => {
    await itemDetailPage.gotoById(paperTowelsId);
    await itemDetailPage.useStock(areas.pantry, '5');

    await expect(itemDetailPage.onHandText()).toContainText('1 rolls on hand');
    await expect(itemDetailPage.lowBadge()).toBeVisible();
    await expect(itemDetailPage.stockRow(areas.pantry)).toContainText('1 rolls');
    await expect(itemDetailPage.movementRows()).toHaveCount(2);
  });

  test('Browse surfaces the low consumable under Needs attention', async ({ browsePage, itemListPage }) => {
    await browsePage.goto();
    await browsePage.expandNeedsAttention();

    const lowStock = browsePage.lowStockSection();
    await expect(lowStock).toContainText(consumables.paperTowels);
    await expect(lowStock).not.toContainText(consumables.batteries);

    await lowStock.getByRole('link', { name: /View all/ }).click();
    // The list seeds its filters from the deep link and then strips the params from the URL, so
    // the applied filter is read off the trigger rather than the address bar.
    await expect(browsePage.page).toHaveURL(/\/inventory\/items/);
    await expect(itemListPage.filterButton).toContainText('Filter: Low stock');
    await expect(itemListPage.item(consumables.paperTowels)).toBeVisible();
    await expect(itemListPage.item(consumables.batteries)).toHaveCount(0);
  });

  test('tracking one individually moves a unit out of stock into a new durable', async ({
    itemDetailPage,
    request,
  }) => {
    await itemDetailPage.gotoById(batteries.id);
    await expect(itemDetailPage.onHandText()).toContainText('4 batteries on hand');

    const durableId = await itemDetailPage.trackOne(areas.pantry, durables.trackedBattery);

    await expect(itemDetailPage.onHandText()).toContainText('3 batteries on hand');
    await expect(itemDetailPage.stockRow(areas.pantry)).toContainText('3 batteries');
    await expect(itemDetailPage.movementRows().filter({ hasText: durables.trackedBattery })).toHaveCount(1);
    const { consumable } = await getConsumableViaApi(request, batteries.id);
    expect(consumable.totalOnHand).toBe(3);

    // The carved-out unit is its own durable, placed where the stock was.
    await itemDetailPage.gotoById(durableId);
    await expect(itemDetailPage.nameHeading()).toHaveText(durables.trackedBattery);
    await expect(itemDetailPage.locationPath()).toContainText(areas.pantry);
  });
});
