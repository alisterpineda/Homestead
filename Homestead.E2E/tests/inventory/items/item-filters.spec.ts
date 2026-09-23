import { test, expect } from '../../../fixtures/inventory.fixture';
import { TEST_DATA } from '../../../e2e.config';
import {
  createAreaViaApi,
  createConsumableViaApi,
  createDurableViaApi,
  resetInventoryViaApi,
} from '../../../helpers/api/inventory';

const { areas, consumables, durables, tags } = TEST_DATA.inventory;

test.describe.configure({ mode: 'serial' });

test.describe('Inventory — items list search and tag filter', () => {
  test.beforeAll(async ({ request }) => {
    await resetInventoryViaApi(request);
    const garage = await createAreaViaApi(request, areas.garage);
    await createDurableViaApi(request, { name: durables.drill, areaId: garage.id, tags: [tags.powerTools] });
    await createDurableViaApi(request, { name: durables.headlamp, areaId: garage.id, tags: [tags.camping] });
    await createConsumableViaApi(request, {
      name: consumables.batteries,
      unit: 'batteries',
      tags: [tags.tools],
      initialQuantity: 8,
      initialAreaId: garage.id,
    });
  });

  test.afterAll(async ({ request }) => {
    await resetInventoryViaApi(request);
  });

  test('lists durables and consumables together', async ({ itemListPage }) => {
    await itemListPage.goto();
    await expect(itemListPage.item(durables.drill)).toBeVisible();
    await expect(itemListPage.item(durables.headlamp)).toBeVisible();
    await expect(itemListPage.item(consumables.batteries)).toBeVisible();
  });

  test('search narrows the list by name', async ({ itemListPage }) => {
    await itemListPage.goto();
    // Wait for the server's filtered answers: while a search is in flight the list page hides
    // non-matching rows client-side, so the negative assertions alone could pass before (or
    // without) the backend ever applying the search.
    const searched = (resource: 'durables' | 'consumables') =>
      itemListPage.page.waitForResponse(
        (resp) =>
          resp.url().includes(`/api/v1/inventory/${resource}`) &&
          resp.url().includes('Search=Headlamp') &&
          resp.request().method() === 'GET' &&
          resp.status() === 200,
      );
    const responses = Promise.all([searched('durables'), searched('consumables')]);
    await itemListPage.searchInput.fill('Headlamp');
    await responses;

    await expect(itemListPage.item(durables.headlamp)).toBeVisible();
    await expect(itemListPage.item(durables.drill)).toHaveCount(0);
    await expect(itemListPage.item(consumables.batteries)).toHaveCount(0);
  });

  test('a tag filter matches the tag itself, and its sub-tags only when asked', async ({ itemListPage }) => {
    await itemListPage.goto();
    await itemListPage.toggleTagFilter(tags.tools);

    // Exact match by default: the consumable carries e2e-tools, the drill only e2e-tools/power.
    await expect(itemListPage.item(consumables.batteries)).toBeVisible();
    await expect(itemListPage.item(durables.drill)).toHaveCount(0);
    await expect(itemListPage.item(durables.headlamp)).toHaveCount(0);

    await itemListPage.setIncludeSubTags(true);
    await expect(itemListPage.item(consumables.batteries)).toBeVisible();
    await expect(itemListPage.item(durables.drill)).toBeVisible();
    await expect(itemListPage.item(durables.headlamp)).toHaveCount(0);
  });
});
