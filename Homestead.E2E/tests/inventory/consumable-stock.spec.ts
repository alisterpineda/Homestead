import { test, expect } from '../../fixtures/inventory.fixture';
import { createAreaViaApi, createConsumableViaApi, getConsumableViaApi } from '../../helpers/api/inventory';

test.describe('Inventory — consumable stock', () => {
  test('creates a consumable with starting stock and a restock threshold', async ({
    request,
    itemListPage,
    itemDetailPage,
    uid,
  }) => {
    const pantry = await createAreaViaApi(request, `Pantry ${uid}`);
    const paperTowels = `Paper Towels ${uid}`;

    await itemListPage.goto();
    await itemListPage.createItemDialog.open(itemListPage.newItemButton.first());
    const id = await itemListPage.createItemDialog.create({
      kind: 'Consumable',
      name: paperTowels,
      location: pantry.name,
      startingStock: '6',
      unit: 'rolls',
      restockThreshold: '2',
    });

    await itemDetailPage.gotoById(id);
    await expect(itemDetailPage.nameHeading()).toHaveText(paperTowels);
    await expect(itemDetailPage.onHandText()).toContainText('6 rolls on hand');
    await expect(itemDetailPage.lowBadge()).toHaveCount(0);
    await expect(itemDetailPage.stockRow(pantry.name)).toContainText('6 rolls');
    // The starting stock is a real inflow on the ledger, not a bare count.
    await expect(itemDetailPage.movementRows()).toHaveCount(1);
  });

  test('using stock down to the threshold flags it as low', async ({ request, itemDetailPage, uid }) => {
    const pantry = await createAreaViaApi(request, `Pantry ${uid}`);
    const paperTowels = await createConsumableViaApi(request, {
      name: `Paper Towels ${uid}`,
      unit: 'rolls',
      restockThreshold: 2,
      initialQuantity: 6,
      initialAreaId: pantry.id,
    });

    await itemDetailPage.gotoById(paperTowels.id);
    await itemDetailPage.useStock(pantry.name, '5');

    await expect(itemDetailPage.onHandText()).toContainText('1 rolls on hand');
    await expect(itemDetailPage.lowBadge()).toBeVisible();
    await expect(itemDetailPage.stockRow(pantry.name)).toContainText('1 rolls');
    await expect(itemDetailPage.movementRows()).toHaveCount(2);
  });

  test('the low-stock "View all" link lands on the items list filtered to low stock', async ({
    request,
    browsePage,
    itemListPage,
    uid,
  }) => {
    const pantry = await createAreaViaApi(request, `Pantry ${uid}`);
    const low = await createConsumableViaApi(request, {
      name: `Paper Towels ${uid}`,
      unit: 'rolls',
      restockThreshold: 2,
      initialQuantity: 1,
      initialAreaId: pantry.id,
    });
    const stocked = await createConsumableViaApi(request, {
      name: `AA Batteries ${uid}`,
      unit: 'batteries',
      restockThreshold: 2,
      initialQuantity: 8,
      initialAreaId: pantry.id,
    });

    await browsePage.goto();
    await browsePage.expandNeedsAttention();
    await browsePage.lowStockSection().getByRole('link', { name: /View all/ }).click();

    // The list seeds its filters from the deep link and then strips the params from the URL, so
    // the applied filter is read off the trigger rather than the address bar.
    await expect(browsePage.page).toHaveURL(/\/inventory\/items/);
    await expect(itemListPage.filterButton).toContainText('Filter: Low stock');
    // Other tests' low items share the list, so narrow it to this test's pair by name.
    await itemListPage.search(uid);
    await expect(itemListPage.item(low.name)).toBeVisible();
    await expect(itemListPage.item(stocked.name)).toHaveCount(0);
  });

  test('tracking one individually moves a unit out of stock into a new durable', async ({
    request,
    itemDetailPage,
    uid,
  }) => {
    const pantry = await createAreaViaApi(request, `Pantry ${uid}`);
    const batteries = await createConsumableViaApi(request, {
      name: `AA Batteries ${uid}`,
      unit: 'batteries',
      initialQuantity: 4,
      initialAreaId: pantry.id,
    });
    const tracked = `Battery #1 ${uid}`;

    await itemDetailPage.gotoById(batteries.id);
    await expect(itemDetailPage.onHandText()).toContainText('4 batteries on hand');

    const durableId = await itemDetailPage.trackOne(pantry.name, tracked);

    await expect(itemDetailPage.onHandText()).toContainText('3 batteries on hand');
    await expect(itemDetailPage.stockRow(pantry.name)).toContainText('3 batteries');
    await expect(itemDetailPage.movementRows().filter({ hasText: tracked })).toHaveCount(1);
    const { consumable } = await getConsumableViaApi(request, batteries.id);
    expect(consumable.totalOnHand).toBe(3);

    // The carved-out unit is its own durable, placed where the stock was.
    await itemDetailPage.gotoById(durableId);
    await expect(itemDetailPage.nameHeading()).toHaveText(tracked);
    await expect(itemDetailPage.locationPath()).toContainText(pantry.name);
  });
});
