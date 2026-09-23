import { test, expect } from '../../fixtures/inventory.fixture';
import { createAreaViaApi, createDurableViaApi } from '../../helpers/api/inventory';
import { stubEmptyList } from '../../helpers/network';

test.describe('Inventory — places, containers and moving with contents', () => {
  test('shows the empty state when there are no areas', async ({ page, areaListPage }) => {
    await stubEmptyList(page, /\/api\/v1\/inventory\/areas\?/);
    await areaListPage.goto();
    await expect(areaListPage.noAreasEmpty).toBeVisible();
    await expect(areaListPage.newAreaButton.first()).toBeVisible();
  });

  test('creates top-level areas and a nested sub-area', async ({ areaListPage, uid }) => {
    const garage = `Garage ${uid}`;
    const basement = `Basement ${uid}`;
    const workshop = `Workshop ${uid}`;

    await areaListPage.goto();
    await areaListPage.createArea(garage);
    await areaListPage.createArea(basement);
    await areaListPage.createArea(workshop, basement);

    await expect(areaListPage.rowLink(garage)).toBeVisible();
    await expect(areaListPage.parentCaption(basement)).toBeVisible();
  });

  test('turns a durable into a container and stores an item inside it', async ({
    request,
    itemListPage,
    itemDetailPage,
    uid,
  }) => {
    const garage = await createAreaViaApi(request, `Garage ${uid}`);
    const toolbox = `Toolbox ${uid}`;
    const drill = `Drill ${uid}`;

    await itemListPage.goto();
    await itemListPage.createItemDialog.open(itemListPage.newItemButton.first());
    const toolboxId = await itemListPage.createItemDialog.create({ kind: 'Durable', name: toolbox, location: garage.name });

    await itemDetailPage.gotoById(toolboxId);
    await expect(itemDetailPage.nameHeading()).toHaveText(toolbox);
    await expect(itemDetailPage.locationPath()).toContainText(garage.name);

    await itemDetailPage.storeItemsInside();
    await expect(itemDetailPage.contentsSection()).toContainText('This container is empty.');

    const dialog = await itemDetailPage.openAddItemToContainer();
    // "Add item" from a container pre-fills that container as the new item's location.
    await expect(dialog.locationTrigger()).toContainText(toolbox);
    await dialog.create({ kind: 'Durable', name: drill });

    await expect(itemDetailPage.contentsSection().getByText(drill, { exact: true })).toBeVisible();
  });

  test('moving a container carries its contents to the new area', async ({ request, itemDetailPage, uid }) => {
    const garage = await createAreaViaApi(request, `Garage ${uid}`);
    const basement = await createAreaViaApi(request, `Basement ${uid}`);
    const workshop = await createAreaViaApi(request, `Workshop ${uid}`, basement.id);
    const toolbox = await createDurableViaApi(request, { name: `Toolbox ${uid}`, areaId: garage.id, isContainer: true });
    const drill = await createDurableViaApi(request, { name: `Drill ${uid}`, parentItemId: toolbox.id });

    await itemDetailPage.gotoById(toolbox.id);
    await itemDetailPage.moveDurable(workshop.name);

    const toolboxPath = itemDetailPage.locationPath();
    await expect(toolboxPath).toContainText(basement.name);
    await expect(toolboxPath).toContainText(workshop.name);
    await expect(toolboxPath).not.toContainText(garage.name);

    // The drill was never edited: its location is derived through the container, so it follows.
    await itemDetailPage.gotoById(drill.id);
    const drillPath = itemDetailPage.locationPath();
    await expect(drillPath).toContainText(basement.name);
    await expect(drillPath).toContainText(workshop.name);
    await expect(drillPath).toContainText(toolbox.name);
    await expect(drillPath).not.toContainText(garage.name);
  });

  test('Browse drills from area to sub-area to container and finds the stored item', async ({
    request,
    browsePage,
    uid,
  }) => {
    const basement = await createAreaViaApi(request, `Basement ${uid}`);
    const workshop = await createAreaViaApi(request, `Workshop ${uid}`, basement.id);
    const toolbox = await createDurableViaApi(request, { name: `Toolbox ${uid}`, areaId: workshop.id, isContainer: true });
    const drill = await createDurableViaApi(request, { name: `Drill ${uid}`, parentItemId: toolbox.id });

    await browsePage.goto();
    await expect(browsePage.placeCard(basement.name)).toBeVisible();
    // Only root areas sit on the Browse root; the sub-area is one level down.
    await expect(browsePage.placeCard(workshop.name)).toHaveCount(0);

    await browsePage.drillInto(basement.name);
    await browsePage.drillInto(workshop.name);
    await browsePage.drillInto(toolbox.name);

    await expect(browsePage.looseItem(drill.name)).toBeVisible();
    const breadcrumb = browsePage.breadcrumb();
    await expect(breadcrumb).toContainText(basement.name);
    await expect(breadcrumb).toContainText(workshop.name);
    await expect(breadcrumb).toContainText(toolbox.name);
  });
});
