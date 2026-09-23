import { test, expect } from '../../fixtures/inventory.fixture';
import {
  createAreaViaApi,
  createConsumableViaApi,
  createDurableViaApi,
  getDurableViaApi,
  patchDurableViaApi,
} from '../../helpers/api/inventory';

test.describe('Inventory — Browse overview and Unplaced', () => {
  test('the module root lands on Browse, where an area card counts everything that lives there', async ({
    request,
    page,
    browsePage,
    uid,
  }) => {
    const shed = await createAreaViaApi(request, `Shed ${uid}`);
    const bench = await createAreaViaApi(request, `Potting Bench ${uid}`, shed.id);
    const toolbox = await createDurableViaApi(request, { name: `Toolbox ${uid}`, areaId: shed.id, isContainer: true });
    await createDurableViaApi(request, { name: `Trowel ${uid}`, parentItemId: toolbox.id });
    await createDurableViaApi(request, { name: `Mower ${uid}`, areaId: bench.id });
    const ladder = await createDurableViaApi(request, { name: `Ladder ${uid}`, areaId: shed.id });
    await patchDurableViaApi(request, ladder.id, { status: 'Lent', statusNote: 'Dave' });
    const rake = await createDurableViaApi(request, { name: `Rake ${uid}`, areaId: shed.id });
    await patchDurableViaApi(request, rake.id, { status: 'Archived' });
    await createConsumableViaApi(request, {
      name: `Seed Packets ${uid}`,
      restockThreshold: 5,
      initialQuantity: 1,
      initialAreaId: shed.id,
    });

    await browsePage.goto('/inventory');
    await expect(page).toHaveURL(/\/inventory\/browse$/);

    // Resolved semantics: the toolbox, the trowel inside it, the sub-area's mower, the lent ladder
    // (lent is still live) and the seed packets — but not the archived rake.
    await expect(browsePage.placeCardCount(shed.name)).toHaveText('5');
    await expect(browsePage.placeCardLowStockBadge(shed.name)).toHaveAttribute('aria-label', '1 low stock');
    // Sub-areas are one level down, not on the root grid.
    await expect(browsePage.placeCard(bench.name)).toHaveCount(0);

    await browsePage.drillInto(shed.name);
    await expect(browsePage.placeCardCount(bench.name)).toHaveText('1');
  });

  test('an unplaced item shows under Unplaced and leaves once placed', async ({
    request,
    browsePage,
    uid,
  }) => {
    const garage = await createAreaViaApi(request, `Garage ${uid}`);
    const box = await createDurableViaApi(request, { name: `Moving Box ${uid}` });

    await browsePage.goto();
    await browsePage.drillInto('Unplaced');
    await expect(browsePage.looseItem(box.name)).toBeVisible();

    const panel = await browsePage.openQuickPanel(box.name);
    await expect(panel.locator('app-location-path')).toContainText('Unplaced');
    await browsePage.moveFromQuickPanel(panel, garage.name);

    await expect(browsePage.looseItem(box.name)).toHaveCount(0);
    expect((await getDurableViaApi(request, box.id)).areaId).toBe(garage.id);

    await browsePage.goto();
    await browsePage.drillInto(garage.name);
    await expect(browsePage.looseItem(box.name)).toBeVisible();
  });
});
