import { test, expect } from '../../fixtures/inventory.fixture';
import {
  createAreaViaApi,
  createBrandViaApi,
  createDurableViaApi,
  findBrandsViaApi,
  getDurableViaApi,
} from '../../helpers/api/inventory';

test.describe('Inventory — brands', () => {
  test('a brand created from the item form picker is attributed and listed on the Brands page', async ({
    request,
    itemDetailPage,
    brandListPage,
    uid,
  }) => {
    const garage = await createAreaViaApi(request, `Garage ${uid}`);
    const drill = await createDurableViaApi(request, { name: `Drill ${uid}`, areaId: garage.id });
    const makita = `Makita ${uid}`;

    await itemDetailPage.gotoById(drill.id);
    await itemDetailPage.createAndSetBrand(makita);
    await expect(itemDetailPage.detail('Brand')).toHaveText(makita);

    // Find-or-create made one canonical row, and it already counts the item.
    await brandListPage.goto();
    await brandListPage.search(uid);
    await expect(brandListPage.nameLink(makita)).toBeVisible();
    await expect(brandListPage.itemCount(makita)).toHaveText('1');
  });

  test('renames a brand', async ({ request, brandListPage, uid }) => {
    const typo = await createBrandViaApi(request, `Bosh ${uid}`);
    const bosch = `Bosch ${uid}`;

    await brandListPage.goto();
    await brandListPage.search(uid);
    await brandListPage.rename(typo.name, bosch);

    await expect(brandListPage.nameLink(bosch)).toBeVisible();
    await expect(brandListPage.nameLink(typo.name)).toHaveCount(0);
    // A rename edits the same row rather than creating another.
    expect(await findBrandsViaApi(request, uid)).toEqual([expect.objectContaining({ id: typo.id, name: bosch })]);
  });

  test('merging moves every item onto the surviving brand and deletes the source', async ({
    request,
    page,
    brandListPage,
    itemListPage,
    uid,
  }) => {
    const apple = await createBrandViaApi(request, `Apple ${uid}`);
    const appleInc = await createBrandViaApi(request, `Apple Inc ${uid}`);
    const laptop = await createDurableViaApi(request, { name: `Laptop ${uid}`, brandId: apple.id });
    const phone = await createDurableViaApi(request, { name: `Phone ${uid}`, brandId: appleInc.id });
    const tablet = await createDurableViaApi(request, { name: `Tablet ${uid}`, brandId: appleInc.id });
    const charger = await createDurableViaApi(request, { name: `Charger ${uid}` });

    await brandListPage.goto();
    await brandListPage.search(uid);
    await expect(brandListPage.itemCount(apple.name)).toHaveText('1');
    await expect(brandListPage.itemCount(appleInc.name)).toHaveText('2');

    await brandListPage.openMerge(appleInc.name);
    await brandListPage.openMergeTargetPicker();
    await brandListPage.chooseMergeTarget(apple.name);
    await expect(brandListPage.mergeDialog).toContainText(
      `Move 2 item(s) from "${appleInc.name}" into "${apple.name}", then delete "${appleInc.name}".`,
    );
    await brandListPage.confirmMerge();

    await expect(brandListPage.nameLink(appleInc.name)).toHaveCount(0);
    await expect(brandListPage.itemCount(apple.name)).toHaveText('3');
    expect((await getDurableViaApi(request, phone.id)).brandId).toBe(apple.id);

    // The survivor's name links to the items list filtered to it: all three items are there.
    await brandListPage.nameLink(apple.name).click();
    await expect(page).toHaveURL(/\/inventory\/items/);
    for (const item of [laptop, phone, tablet]) {
      await expect(itemListPage.item(item.name)).toBeVisible();
    }
    await expect(itemListPage.item(charger.name)).toHaveCount(0);
  });

  test('renaming onto an existing name offers a merge into it instead', async ({ request, brandListPage, uid }) => {
    const sony = await createBrandViaApi(request, `Sony ${uid}`);
    const sonyCorp = await createBrandViaApi(request, `Sony Corp ${uid}`);
    const tv = await createDurableViaApi(request, { name: `TV ${uid}`, brandId: sonyCorp.id });
    // Names are unique case-insensitively, so a differently-cased duplicate collides too.
    const attempted = sony.name.toLowerCase();

    await brandListPage.goto();
    await brandListPage.search(uid);
    const response = await brandListPage.submitRename(sonyCorp.name, attempted);
    expect(response.status()).toBe(409);

    await expect(brandListPage.renameDialog).toContainText(`A brand named "${attempted}" already exists — merge into it?`);
    await brandListPage.renameDialog.getByRole('button', { name: `Merge into "${attempted}"` }).click();

    // The handoff opens the merge dialog with the target search already seeded to the attempted name.
    await expect(brandListPage.mergeDialog.getByRole('heading', { name: 'Merge brand' })).toBeVisible();
    const picker = await brandListPage.openMergeTargetPicker();
    await expect(picker.getByRole('textbox', { name: 'Search brands' })).toHaveValue(attempted);
    await brandListPage.chooseMergeTarget(sony.name, { search: false });
    await brandListPage.confirmMerge();

    await expect(brandListPage.nameLink(sonyCorp.name)).toHaveCount(0);
    await expect(brandListPage.itemCount(sony.name)).toHaveText('1');
    expect((await getDurableViaApi(request, tv.id)).brandId).toBe(sony.id);
  });

  test('delete is blocked while a brand is in use and allowed once it is not', async ({
    request,
    brandListPage,
    uid,
  }) => {
    const dewalt = await createBrandViaApi(request, `Dewalt ${uid}`);
    const ryobi = await createBrandViaApi(request, `Ryobi ${uid}`);
    await createDurableViaApi(request, { name: `Impact Driver ${uid}`, brandId: dewalt.id });

    await brandListPage.goto();
    await brandListPage.search(uid);

    const menu = await brandListPage.openRowMenu(dewalt.name);
    const deleteItem = menu.getByRole('menuitem', { name: 'Delete' });
    await expect(deleteItem).toBeDisabled();
    await expect(deleteItem).toHaveAttribute('title', 'In use by 1 item — merge instead');
    await brandListPage.closeRowMenu();

    const response = await brandListPage.submitDelete(ryobi.name);
    expect(response.status()).toBe(204);
    await expect(brandListPage.deleteDialog.getByRole('heading')).toBeHidden();
    await expect(brandListPage.nameLink(ryobi.name)).toHaveCount(0);
    await expect(brandListPage.nameLink(dewalt.name)).toBeVisible();
    expect((await findBrandsViaApi(request, uid)).map((b) => b.name)).toEqual([dewalt.name]);
  });

  test('a delete the server refuses shows why inside the confirm dialog', async ({
    request,
    brandListPage,
    uid,
  }) => {
    const stanley = await createBrandViaApi(request, `Stanley ${uid}`);

    await brandListPage.goto();
    await brandListPage.search(uid);
    await expect(brandListPage.itemCount(stanley.name)).toHaveText('0');

    // Another member of the household attributes an item after this table loaded, so the row still
    // offers Delete — the server's in-use guard is what catches it.
    await createDurableViaApi(request, { name: `Tape Measure ${uid}`, brandId: stanley.id });

    const response = await brandListPage.submitDelete(stanley.name);
    expect(response.status()).toBe(400);
    const heading = brandListPage.deleteDialog.getByRole('heading', { name: `Delete ${stanley.name}?` });
    await expect(heading).toBeVisible();
    await expect(brandListPage.deleteDialog).toContainText('Brand is in use by one or more items and cannot be deleted.');

    await brandListPage.deleteDialog.getByRole('button', { name: 'Cancel' }).click();
    await expect(heading).toBeHidden();
    await expect(brandListPage.nameLink(stanley.name)).toBeVisible();
    expect(await findBrandsViaApi(request, uid)).toHaveLength(1);
  });
});
