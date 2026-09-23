import { test, expect } from '../../fixtures/inventory.fixture';
import { createAreaViaApi, createDurableViaApi, getDurableViaApi } from '../../helpers/api/inventory';
import { stubFailure } from '../../helpers/network';
import { expectOk } from '../../helpers/playwright.helpers';

const DURABLE_URL = /\/api\/v1\/inventory\/durables\/[0-9a-f-]+$/;

test.describe('Inventory — lending and archiving a durable', () => {
  test('lending records the borrower and lists it under Lent out until it is returned', async ({
    request,
    itemDetailPage,
    browsePage,
    itemListPage,
    uid,
  }) => {
    const garage = await createAreaViaApi(request, `Garage ${uid}`);
    const ladder = await createDurableViaApi(request, { name: `Ladder ${uid}`, areaId: garage.id });
    const drill = await createDurableViaApi(request, { name: `Drill ${uid}`, areaId: garage.id });
    const borrower = `Dave ${uid}`;

    await itemDetailPage.gotoById(ladder.id);
    await itemDetailPage.lend(borrower);
    await expect(itemDetailPage.statusBadge()).toContainText('Lent');
    await expect(itemDetailPage.statusBadge()).toContainText(`to ${borrower}`);
    // Lending isn't moving: it still lives in the garage.
    await expect(itemDetailPage.locationPath()).toContainText(garage.name);

    await browsePage.goto();
    await browsePage.expandNeedsAttention();
    await browsePage.warningSection('Lent out').getByRole('link', { name: /View all/ }).click();
    await expect(itemListPage.filterButton).toContainText('Filter: Lent');
    await itemListPage.search(uid);
    await expect(itemListPage.item(ladder.name)).toBeVisible();
    await expect(itemListPage.item(drill.name)).toHaveCount(0);

    await itemDetailPage.gotoById(ladder.id);
    await itemDetailPage.markReturned();
    await expect(itemDetailPage.statusBadge()).toHaveText('');
    const returned = await getDurableViaApi(request, ladder.id);
    expect(returned.status).toBe('Active');
    expect(returned.statusNote).toBeNull();
  });

  test('archiving hides a durable from the default views until inactive items are shown', async ({
    request,
    itemDetailPage,
    browsePage,
    itemListPage,
    uid,
  }) => {
    const garage = await createAreaViaApi(request, `Garage ${uid}`);
    const drill = await createDurableViaApi(request, { name: `Drill ${uid}`, areaId: garage.id });
    const headlamp = await createDurableViaApi(request, { name: `Headlamp ${uid}`, areaId: garage.id });

    await itemDetailPage.gotoById(headlamp.id);
    await itemDetailPage.archive('Sold to Sam');
    await expect(itemDetailPage.statusBadge()).toContainText('Archived');
    await expect(itemDetailPage.statusBadge()).toContainText('Sold to Sam');

    await browsePage.goto();
    await browsePage.drillInto(garage.name);
    await expect(browsePage.looseItem(drill.name)).toBeVisible();
    await expect(browsePage.looseItem(headlamp.name)).toHaveCount(0);

    await itemListPage.goto();
    await itemListPage.search(uid);
    await expect(itemListPage.item(drill.name)).toBeVisible();
    await expect(itemListPage.item(headlamp.name)).toHaveCount(0);

    await itemListPage.showInactive();
    await expect(itemListPage.item(headlamp.name)).toBeVisible();
    await expect(itemListPage.item(drill.name)).toBeVisible();
    // The record is kept, with the reason given in the same gesture.
    const archived = await getDurableViaApi(request, headlamp.id);
    expect(archived.status).toBe('Archived');
    expect(archived.statusNote).toBe('Sold to Sam');
  });

  test('a lend that fails to save keeps the dialog and the borrower for a retry', async ({
    request,
    page,
    itemDetailPage,
    uid,
  }) => {
    const ladder = await createDurableViaApi(request, { name: `Ladder ${uid}` });
    const borrower = `Dave ${uid}`;

    await itemDetailPage.gotoById(ladder.id);
    await stubFailure(page, DURABLE_URL, { method: 'PATCH' });
    await itemDetailPage.startLend(borrower);
    const failed = await itemDetailPage.submitLend();
    expect(failed.status()).toBe(500);

    const dialog = itemDetailPage.confirmDialog;
    await expect(dialog.getByRole('heading', { name: `Lend ${ladder.name}?` })).toBeVisible();
    await expect(dialog).toContainText('An unexpected server error occurred.');
    await expect(dialog.getByLabel('Lent to')).toHaveValue(borrower);

    // Once the server is back, the same dialog goes through without retyping anything.
    await page.unroute(DURABLE_URL);
    await expectOk(await itemDetailPage.submitLend());
    await expect(dialog.getByRole('heading')).toBeHidden();
    await expect(itemDetailPage.statusBadge()).toContainText(`to ${borrower}`);
    expect((await getDurableViaApi(request, ladder.id)).status).toBe('Lent');
  });
});
