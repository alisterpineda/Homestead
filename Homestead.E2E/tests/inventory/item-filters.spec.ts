import { test, expect } from '../../fixtures/inventory.fixture';
import { createAreaViaApi, createConsumableViaApi, createDurableViaApi } from '../../helpers/api/inventory';

test.describe('Inventory — items list search and tag filter', () => {
  test('lists durables and consumables together', async ({ request, itemListPage, uid }) => {
    const garage = await createAreaViaApi(request, `Garage ${uid}`);
    const drill = await createDurableViaApi(request, { name: `Drill ${uid}`, areaId: garage.id });
    const batteries = await createConsumableViaApi(request, {
      name: `AA Batteries ${uid}`,
      unit: 'batteries',
      initialQuantity: 8,
      initialAreaId: garage.id,
    });

    await itemListPage.goto();
    await itemListPage.search(uid);
    await expect(itemListPage.item(drill.name)).toBeVisible();
    await expect(itemListPage.item(batteries.name)).toBeVisible();
  });

  test('search narrows the list by name', async ({ request, itemListPage, uid }) => {
    const garage = await createAreaViaApi(request, `Garage ${uid}`);
    const drill = await createDurableViaApi(request, { name: `Drill ${uid}`, areaId: garage.id });
    const headlamp = await createDurableViaApi(request, { name: `Headlamp ${uid}`, areaId: garage.id });
    const batteries = await createConsumableViaApi(request, {
      name: `AA Batteries ${uid}`,
      initialQuantity: 8,
      initialAreaId: garage.id,
    });

    await itemListPage.goto();
    await itemListPage.search(headlamp.name);

    await expect(itemListPage.item(headlamp.name)).toBeVisible();
    await expect(itemListPage.item(drill.name)).toHaveCount(0);
    await expect(itemListPage.item(batteries.name)).toHaveCount(0);
  });

  test('a tag filter matches the tag itself, and its sub-tags only when asked', async ({
    request,
    itemListPage,
    uid,
  }) => {
    const tools = `e2e-tools-${uid}`;
    const garage = await createAreaViaApi(request, `Garage ${uid}`);
    const drill = await createDurableViaApi(request, { name: `Drill ${uid}`, areaId: garage.id, tags: [`${tools}/power`] });
    const headlamp = await createDurableViaApi(request, {
      name: `Headlamp ${uid}`,
      areaId: garage.id,
      tags: [`e2e-camping-${uid}`],
    });
    const batteries = await createConsumableViaApi(request, {
      name: `AA Batteries ${uid}`,
      tags: [tools],
      initialQuantity: 8,
      initialAreaId: garage.id,
    });

    await itemListPage.goto();
    await itemListPage.toggleTagFilter(tools);

    // Exact match by default: the consumable carries the tag itself, the drill only its sub-tag.
    await expect(itemListPage.item(batteries.name)).toBeVisible();
    await expect(itemListPage.item(drill.name)).toHaveCount(0);
    await expect(itemListPage.item(headlamp.name)).toHaveCount(0);

    await itemListPage.includeSubTags();
    await expect(itemListPage.item(batteries.name)).toBeVisible();
    await expect(itemListPage.item(drill.name)).toBeVisible();
    await expect(itemListPage.item(headlamp.name)).toHaveCount(0);
  });
});
