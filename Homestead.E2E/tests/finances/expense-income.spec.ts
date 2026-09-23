import { test, expect } from '../../fixtures/finances.fixture';
import { DISPLAY_CURRENCY } from '../../e2e.config';
import {
  createAccountViaApi,
  createCategoryViaApi,
  createFlowViaApi,
  createPayeeViaApi,
  findCurrencyId,
  getFlowViaApi,
  listAccountFlowsViaApi,
} from '../../helpers/api/finances';
import { isoDateDaysAgo, todayIso } from '../../helpers/date.helpers';
import { stubFailure } from '../../helpers/network';
import { formatBalance } from '../../pages/finances/account-detail.page';

test.describe('Finances — expenses and income', () => {
  test('recording an expense for a payee autofills its default category and lowers the balance', async ({
    request,
    accountDetailPage,
    uid,
  }) => {
    const cad = await findCurrencyId(request, DISPLAY_CURRENCY);
    const chequing = await createAccountViaApi(request, {
      name: `Chequing ${uid}`,
      kind: 'Cash',
      reportingCurrencyId: cad,
      initialBalance: { amount: 1000, asOfDate: isoDateDaysAgo(30) },
    });
    const groceries = await createCategoryViaApi(request, { name: `Groceries ${uid}`, type: 'Expense' });
    const market = await createPayeeViaApi(request, { name: `Corner Market ${uid}`, defaultCategoryId: groceries.id });
    const memo = `Weekly shop ${uid}`;

    await accountDetailPage.gotoById(chequing.id);
    const sheet = await accountDetailPage.openNewFlow('Expense');
    await sheet.pickPayee(market.name);
    // The payee's default category is pre-selected — the user didn't pick one.
    await expect(sheet.categoryTrigger).toContainText(groceries.name);
    await sheet.fill({ amount: '42.50', date: todayIso(), description: memo });
    await sheet.submit();

    await expect(accountDetailPage.balanceText()).toHaveText(formatBalance(957.5));
    await expect(accountDetailPage.activityRows()).toHaveCount(2);
    const row = accountDetailPage.activityRow(memo);
    await expect(row).toContainText('Expense');
    await expect(row).toContainText(market.name);
    await expect(row).toContainText('42.50');

    const [flow] = await listAccountFlowsViaApi(request, chequing.id);
    expect(flow).toMatchObject({ shape: 'Expense', amount: 42.5, categoryId: groceries.id, payeeId: market.id });
  });

  test('recording income raises the balance', async ({ request, accountDetailPage, uid }) => {
    const cad = await findCurrencyId(request, DISPLAY_CURRENCY);
    const chequing = await createAccountViaApi(request, {
      name: `Chequing ${uid}`,
      kind: 'Cash',
      reportingCurrencyId: cad,
      initialBalance: { amount: 1000, asOfDate: isoDateDaysAgo(30) },
    });
    const salary = await createCategoryViaApi(request, { name: `Salary ${uid}`, type: 'Income' });
    const memo = `Pay day ${uid}`;

    await accountDetailPage.gotoById(chequing.id);
    const sheet = await accountDetailPage.openNewFlow('Income');
    await sheet.pickCategory(salary.name);
    await sheet.fill({ amount: '2500.00', date: todayIso(), description: memo });
    await sheet.submit();

    await expect(accountDetailPage.balanceText()).toHaveText(formatBalance(3500));
    await expect(accountDetailPage.activityRow(memo)).toContainText('Income');

    const [flow] = await listAccountFlowsViaApi(request, chequing.id);
    expect(flow).toMatchObject({ shape: 'Income', amount: 2500, categoryId: salary.id });
  });

  test('editing an expense re-computes the balance, and deleting it restores the original', async ({
    request,
    accountDetailPage,
    uid,
  }) => {
    const cad = await findCurrencyId(request, DISPLAY_CURRENCY);
    const card = await createAccountViaApi(request, {
      name: `Visa ${uid}`,
      kind: 'CreditCard',
      reportingCurrencyId: cad,
      initialBalance: { amount: 500, asOfDate: isoDateDaysAgo(30) },
    });
    const memo = `Dinner ${uid}`;
    const flow = await createFlowViaApi(request, {
      direction: 'Expense',
      accountId: card.id,
      amount: 60,
      date: isoDateDaysAgo(2),
      description: memo,
    });

    await accountDetailPage.gotoById(card.id);
    // A charge on a credit card adds to the amount owed.
    await expect(accountDetailPage.balanceText()).toHaveText(formatBalance(560));

    let sheet = await accountDetailPage.openFlow(memo);
    // Direction is immutable once recorded.
    await expect(sheet.dialog.getByRole('radio', { name: /Income/ })).toBeDisabled();
    await sheet.fill({ amount: '75.25' });
    await sheet.submit();

    await expect(accountDetailPage.balanceText()).toHaveText(formatBalance(575.25));
    await expect(accountDetailPage.activityRow(memo)).toContainText('75.25');
    expect((await getFlowViaApi(request, flow.id)).amount).toBe(75.25);

    sheet = await accountDetailPage.openFlow(memo);
    await sheet.delete();

    await expect(accountDetailPage.balanceText()).toHaveText(formatBalance(500));
    await expect(accountDetailPage.activityRow(memo)).toHaveCount(0);
    await expect(accountDetailPage.activityRows()).toHaveCount(1);
  });

  test('a failed save keeps the sheet open, reports the error, and records nothing', async ({
    page,
    request,
    accountDetailPage,
    uid,
  }) => {
    const cad = await findCurrencyId(request, DISPLAY_CURRENCY);
    const chequing = await createAccountViaApi(request, {
      name: `Chequing ${uid}`,
      kind: 'Cash',
      reportingCurrencyId: cad,
      initialBalance: { amount: 1000, asOfDate: isoDateDaysAgo(30) },
    });

    await accountDetailPage.gotoById(chequing.id);
    const sheet = await accountDetailPage.openNewFlow('Expense');
    // No amount yet: the form can't be submitted.
    await expect(sheet.submitButton).toBeDisabled();
    await sheet.fill({ amount: '0', date: todayIso(), description: `Coffee ${uid}` });
    await expect(sheet.submitButton).toBeDisabled();

    await sheet.fill({ amount: '4.75' });
    await stubFailure(page, /\/api\/v1\/finances\/transactions\/flows$/, { method: 'POST' });
    await sheet.submitButton.click();

    await expect(page.getByText('An unexpected server error occurred. Please try again later.')).toBeVisible();
    await expect(sheet.heading).toBeVisible();
    await expect(sheet.submitButton).toBeEnabled();
    await expect(sheet.amountInput).toHaveValue('4.75');

    await page.keyboard.press('Escape');
    await expect(sheet.heading).toBeHidden();
    await expect(accountDetailPage.balanceText()).toHaveText(formatBalance(1000));
    expect(await listAccountFlowsViaApi(request, chequing.id)).toHaveLength(0);
  });
});
