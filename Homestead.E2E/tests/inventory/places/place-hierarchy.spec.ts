import { test, expect } from '../../../fixtures/inventory.fixture';
import { TEST_DATA } from '../../../e2e.config';
import { resetInventoryViaApi } from '../../../helpers/api/inventory';

const { areas, durables } = TEST_DATA.inventory;

test.describe.configure({ mode: 'serial' });

test.describe('Inventory — places, containers and moving with contents', () => {
  let toolboxId: string;
  let drillId: string;

  test.beforeAll(async ({ request }) => {
    await resetInventoryViaApi(request);
  });

  test.afterAll(async ({ request }) => {
    await resetInventoryViaApi(request);
  });

  test('shows empty state when no areas exist', async ({ areaListPage }) => {
    await areaListPage.goto();
    await expect(areaListPage.noAreasEmpty).toBeVisible();
  });

  test('creates top-level areas and a nested sub-area', async ({ areaListPage }) => {
    await areaListPage.goto();
    await areaListPage.createArea(areas.garage);
    await areaListPage.createArea(areas.basement);
    await areaListPage.createArea(areas.workshop, areas.basement);

    await expect(areaListPage.parentCaption(areas.basement)).toBeVisible();
  });

  test('turns a durable into a container and stores an item inside it', async ({
    itemListPage,
    itemDetailPage,
  }) => {
    await itemListPage.goto();
    await itemListPage.createItemDialog.open(itemListPage.newItemButton.first());
    toolboxId = await itemListPage.createItemDialog.create({
      kind: 'Durable',
      name: durables.toolbox,
      location: areas.garage,
    });

    await itemDetailPage.gotoById(toolboxId);
    await expect(itemDetailPage.nameHeading()).toHaveText(durables.toolbox);
    await expect(itemDetailPage.locationPath()).toContainText(areas.garage);

    await itemDetailPage.storeItemsInside();
    await expect(itemDetailPage.contentsSection()).toContainText('This container is empty.');

    const dialog = await itemDetailPage.openAddItemToContainer();
    // "Add item" from a container pre-fills that container as the new item's location.
    await expect(dialog.locationTrigger()).toContainText(durables.toolbox);
    drillId = await dialog.create({ kind: 'Durable', name: durables.drill });

    await expect(itemDetailPage.contentsSection().getByText(durables.drill, { exact: true })).toBeVisible();
  });

  test('moving the container carries its contents to the new area', async ({ itemDetailPage }) => {
    await itemDetailPage.gotoById(toolboxId);
    await itemDetailPage.moveDurable(areas.workshop);

    const toolboxPath = itemDetailPage.locationPath();
    await expect(toolboxPath).toContainText(areas.basement);
    await expect(toolboxPath).toContainText(areas.workshop);
    await expect(toolboxPath).not.toContainText(areas.garage);

    // The drill was never edited: its location is derived through the container, so it follows.
    await itemDetailPage.gotoById(drillId);
    const drillPath = itemDetailPage.locationPath();
    await expect(drillPath).toContainText(areas.basement);
    await expect(drillPath).toContainText(areas.workshop);
    await expect(drillPath).toContainText(durables.toolbox);
    await expect(drillPath).not.toContainText(areas.garage);
  });

  test('browse drills from area to sub-area to container and finds the stored item', async ({
    browsePage,
  }) => {
    await browsePage.goto();
    await expect(browsePage.placeCard(areas.garage)).toBeVisible();
    // Only root areas sit on the Browse root; the sub-area is one level down.
    await expect(browsePage.placeCard(areas.workshop)).toHaveCount(0);

    await browsePage.drillInto(areas.basement);
    await browsePage.drillInto(areas.workshop);
    await browsePage.drillInto(durables.toolbox);

    await expect(browsePage.looseItem(durables.drill)).toBeVisible();
    const breadcrumb = browsePage.breadcrumb();
    await expect(breadcrumb).toContainText(areas.basement);
    await expect(breadcrumb).toContainText(areas.workshop);
    await expect(breadcrumb).toContainText(durables.toolbox);
  });
});
