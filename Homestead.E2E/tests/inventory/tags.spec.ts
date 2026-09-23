import { test, expect } from '../../fixtures/inventory.fixture';
import { createDurableViaApi, findTagsViaApi, getDurableViaApi } from '../../helpers/api/inventory';

test.describe('Inventory — tags', () => {
  test('creating a nested path adds its parent, and renaming the parent re-paths the child', async ({
    request,
    inventoryTagListPage,
    uid,
  }) => {
    const garden = `e2e-garden-${uid}`;
    const yard = `e2e-yard-${uid}`;

    await inventoryTagListPage.goto();
    await inventoryTagListPage.createTag(`${garden}/hoses`);
    await inventoryTagListPage.search(uid);
    // The missing ancestor is upserted in the same operation.
    await expect(inventoryTagListPage.tagLink(garden)).toBeVisible();
    await expect(inventoryTagListPage.tagLink(`${garden}/hoses`)).toBeVisible();

    await inventoryTagListPage.rename(garden, yard);

    await expect(inventoryTagListPage.tagLink(yard)).toBeVisible();
    await expect(inventoryTagListPage.tagLink(`${yard}/hoses`)).toBeVisible();
    await expect(inventoryTagListPage.tagLink(`${garden}/hoses`)).toHaveCount(0);
    const paths = (await findTagsViaApi(request, uid)).map((t) => t.path).sort();
    expect(paths).toEqual([yard, `${yard}/hoses`]);
  });

  test('deleting a tag un-tags its items and re-parents its children', async ({
    request,
    inventoryTagListPage,
    uid,
  }) => {
    const tools = `e2e-tools-${uid}`;
    const drill = await createDurableViaApi(request, { name: `Drill ${uid}`, tags: [`${tools}/power`] });
    const saw = await createDurableViaApi(request, { name: `Saw ${uid}`, tags: [`${tools}/power/cordless`] });

    await inventoryTagListPage.goto();
    await inventoryTagListPage.search(uid);
    await inventoryTagListPage.select('power');
    await inventoryTagListPage.deleteSelected();

    await expect(inventoryTagListPage.tagLink(`${tools}/power`)).toHaveCount(0);
    await expect(inventoryTagListPage.tagLink(`${tools}/cordless`)).toBeVisible();
    await expect(inventoryTagListPage.tagLink(tools)).toBeVisible();

    // Delete is destructive for the tag only: both items survive, the drill simply loses its tag,
    // and the saw's tag moved up under the deleted tag's parent.
    expect((await getDurableViaApi(request, drill.id)).tagPaths).toEqual([]);
    expect((await getDurableViaApi(request, saw.id)).tagPaths).toEqual([`${tools}/cordless`]);
  });
});
