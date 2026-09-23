import { test, expect } from '../../fixtures/inventory.fixture';
import {
  createAreaViaApi,
  createConsumableViaApi,
  createDurableViaApi,
  createWarrantyViaApi,
  getDurableViaApi,
} from '../../helpers/api/inventory';

/** Today (UTC, the server's warranty clock) shifted by whole days and years, as `yyyy-MM-dd`. */
function utcDate({ days = 0, years = 0 }: { days?: number; years?: number }): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + days);
  d.setUTCFullYear(d.getUTCFullYear() + years);
  return d.toISOString().slice(0, 10);
}

test.describe('Inventory — warranties', () => {
  test('a warranty lapsing within 30 days reads Expiring, and the Warranty facet and tile find it', async ({
    request,
    itemDetailPage,
    itemListPage,
    browsePage,
    uid,
  }) => {
    const kitchen = await createAreaViaApi(request, `Kitchen ${uid}`);
    // Bought a year ago less ten days, so the "1 year" preset lands ten days from now.
    const dishwasher = await createDurableViaApi(request, {
      name: `Dishwasher ${uid}`,
      acquiredOn: utcDate({ days: 10, years: -1 }),
    });
    const fridge = await createDurableViaApi(request, { name: `Fridge ${uid}` });
    await createWarrantyViaApi(request, fridge.id, { expiresOn: utcDate({ years: 3 }) });
    const salt = await createConsumableViaApi(request, {
      name: `Dishwasher Salt ${uid}`,
      initialQuantity: 2,
      initialAreaId: kitchen.id,
    });

    await itemDetailPage.gotoById(dishwasher.id);
    const warranties = itemDetailPage.warranties;
    await expect(warranties.root).toContainText('No warranties recorded.');
    // The expiry is the one field that matters, so Add waits for an explicit choice.
    await expect(warranties.addButton).toBeDisabled();
    const preset = await warranties.chooseYearPreset(1);
    const expiry = preset.split(' · ')[1];
    await expect(warranties.expiryChip).toHaveAccessibleName(`Expiry: ${expiry}`);
    await warranties.submit();

    await expect(warranties.rows()).toHaveCount(1);
    const row = warranties.rows().first();
    await expect(row).toContainText('Manufacturer');
    await expect(row).toContainText('Expiring soon');
    await expect(row).toContainText(`until ${expiry}`);
    // Unlike a note's date, the expiry chip resets after each add.
    await expect(warranties.expiryChip).toHaveAccessibleName('Expiry: Expires');
    await expect(warranties.addButton).toBeDisabled();
    expect((await getDurableViaApi(request, dishwasher.id)).warrantyStatus).toBe('Expiring');

    await itemListPage.goto();
    await itemListPage.search(uid);
    await expect(itemListPage.item(fridge.name)).toBeVisible();
    await itemListPage.filterByWarranty('Expiring soon');
    await expect(itemListPage.filterButton).toContainText('Filter: Warranty expiring');
    await expect(itemListPage.item(dishwasher.name)).toBeVisible();
    await expect(itemListPage.item(fridge.name)).toHaveCount(0);
    // Consumables carry no warranty, so any warranty lens drops them.
    await expect(itemListPage.item(salt.name)).toHaveCount(0);

    await browsePage.goto();
    await browsePage.expandNeedsAttention();
    await browsePage.warningSection('Warranty expiring').getByRole('link', { name: /View all/ }).click();
    await expect(itemListPage.filterButton).toContainText('Filter: Warranty expiring');
    await itemListPage.search(uid);
    await expect(itemListPage.item(dishwasher.name)).toBeVisible();
    await expect(itemListPage.item(fridge.name)).toHaveCount(0);
  });
});
