import { test, expect } from '../../fixtures/finances.fixture';
import { DISPLAY_CURRENCY } from '../../e2e.config';
import {
  createAccountViaApi,
  createFlowViaApi,
  createPayeeViaApi,
  findCurrencyId,
  getFlowViaApi,
  searchPayeesViaApi,
} from '../../helpers/api/finances';
import { isoDateDaysAgo } from '../../helpers/date.helpers';

test.describe('Finances — payees', () => {
  test('creates a payee and renames it', async ({ request, payeeListPage, uid }) => {
    const name = `Hydro ${uid}`;
    const renamed = `City Hydro ${uid}`;

    await payeeListPage.goto();
    await payeeListPage.create(name);
    await payeeListPage.search(uid);
    await expect(payeeListPage.row(name)).toBeVisible();

    await payeeListPage.rename(name, renamed);
    await expect(payeeListPage.row(renamed)).toBeVisible();
    await expect(payeeListPage.row(name)).toHaveCount(0);

    expect((await searchPayeesViaApi(request, uid)).map((p) => p.name)).toEqual([renamed]);
  });

  test('deletes an unused payee, but a payee in use can only be archived', async ({ page, request, payeeListPage, uid }) => {
    const cad = await findCurrencyId(request, DISPLAY_CURRENCY);
    const chequing = await createAccountViaApi(request, {
      name: `Chequing ${uid}`,
      kind: 'Cash',
      reportingCurrencyId: cad,
      initialBalance: { amount: 1000, asOfDate: isoDateDaysAgo(30) },
    });
    const unused = await createPayeeViaApi(request, { name: `Old Gym ${uid}` });
    const inUse = await createPayeeViaApi(request, { name: `Bakery ${uid}` });
    await createFlowViaApi(request, {
      direction: 'Expense',
      accountId: chequing.id,
      amount: 8,
      date: isoDateDaysAgo(1),
      payeeId: inUse.id,
    });

    await payeeListPage.goto();
    await payeeListPage.search(uid);
    await payeeListPage.delete(unused.name);
    await expect(payeeListPage.row(unused.name)).toHaveCount(0);

    const deleteInUse = await payeeListPage.rowAction(inUse.name, 'Delete');
    await expect(deleteInUse).toBeDisabled();
    await expect(deleteInUse).toHaveAttribute('title', 'In use by 1 transaction — archive instead');
    await page.keyboard.press('Escape');

    expect((await searchPayeesViaApi(request, uid)).map((p) => p.name)).toEqual([inUse.name]);
  });

  test('renaming a payee onto an existing name offers to merge it, moving its transactions', async ({
    request,
    payeeListPage,
    uid,
  }) => {
    const cad = await findCurrencyId(request, DISPLAY_CURRENCY);
    const chequing = await createAccountViaApi(request, {
      name: `Chequing ${uid}`,
      kind: 'Cash',
      reportingCurrencyId: cad,
      initialBalance: { amount: 1000, asOfDate: isoDateDaysAgo(30) },
    });
    const keeper = await createPayeeViaApi(request, { name: `Blue Bottle Coffee ${uid}` });
    const duplicate = await createPayeeViaApi(request, { name: `Blue Bottle ${uid}` });
    const latte = await createFlowViaApi(request, {
      direction: 'Expense',
      accountId: chequing.id,
      amount: 6.5,
      date: isoDateDaysAgo(1),
      payeeId: duplicate.id,
    });

    await payeeListPage.goto();
    await payeeListPage.search(uid);
    expect(await payeeListPage.submitRename(duplicate.name, keeper.name)).toBe(409);
    await expect(payeeListPage.editDialog.getByText(`A payee named "${keeper.name}" already exists.`)).toBeVisible();

    await payeeListPage.editDialog.getByRole('button', { name: 'Merge this payee into it' }).click();
    await expect(payeeListPage.editHeading).toBeHidden();
    await expect(payeeListPage.row(duplicate.name)).toHaveCount(0);
    await expect(payeeListPage.row(keeper.name)).toBeVisible();

    expect((await getFlowViaApi(request, latte.id)).payeeId).toBe(keeper.id);
    expect((await searchPayeesViaApi(request, uid)).map((p) => p.id)).toEqual([keeper.id]);
  });
});
