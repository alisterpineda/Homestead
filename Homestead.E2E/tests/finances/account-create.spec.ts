import { test, expect } from '../../fixtures/finances.fixture';
import { DISPLAY_CURRENCY } from '../../e2e.config';
import { createAccountViaApi, findCurrencyId, listAccountsViaApi } from '../../helpers/api/finances';
import { isoDateDaysAgo, todayIso } from '../../helpers/date.helpers';
import { stubEmptyList } from '../../helpers/network';
import { KIND_LABELS } from '../../pages/finances/account-list.page';
import { formatBalance } from '../../pages/finances/account-detail.page';

test.describe('Finances — account creation', () => {
  test('shows the empty state when there are no accounts', async ({ page, accountListPage }) => {
    await stubEmptyList(page, /\/api\/v1\/finances\/accounts\?/);
    await accountListPage.goto();
    await expect(accountListPage.noAccountsEmpty).toBeVisible();
    await expect(accountListPage.newAccountButton.first()).toBeVisible();
  });

  test('creates a Cash account with an initial balance', async ({ page, request, accountListPage, accountDetailPage, uid }) => {
    const name = `Chequing ${uid}`;

    await accountListPage.goto();
    await accountListPage.openCreateDialog();
    await accountListPage.submitCreate({
      kind: 'Cash',
      name,
      currencyCode: DISPLAY_CURRENCY,
      initialAmount: '1000.00',
      asOfDate: todayIso(),
    });

    await expect(page).toHaveURL(/\/finances\/accounts\/[0-9a-f-]+/);
    await expect(accountDetailPage.nameHeading()).toHaveText(name);
    await expect(accountDetailPage.balanceText()).toHaveText(formatBalance(1000));

    const created = (await listAccountsViaApi(request)).find((a) => a.name === name);
    expect(created?.accountType).toBe('Asset');
  });

  test('creates a Credit Card whose initial balance shows as a positive amount owed', async ({
    request,
    accountListPage,
    accountDetailPage,
    uid,
  }) => {
    const name = `Visa ${uid}`;

    await accountListPage.goto();
    await accountListPage.openCreateDialog();
    await accountListPage.submitCreate({
      kind: 'CreditCard',
      name,
      currencyCode: DISPLAY_CURRENCY,
      initialAmount: '500.00',
      asOfDate: todayIso(),
    });

    await expect(accountDetailPage.nameHeading()).toHaveText(name);
    // Sign-flip: the liability is stored as -500 but displayed as 500 owed.
    await expect(accountDetailPage.balanceText()).toHaveText(formatBalance(500));

    const created = (await listAccountsViaApi(request)).find((a) => a.name === name);
    expect(created?.accountType).toBe('Liability');
  });

  test('the list shows each account with its kind', async ({ request, accountListPage, uid }) => {
    const cad = await findCurrencyId(request, DISPLAY_CURRENCY);
    const asOfDate = isoDateDaysAgo(30);
    const cash = await createAccountViaApi(request, {
      name: `Chequing ${uid}`,
      kind: 'Cash',
      reportingCurrencyId: cad,
      initialBalance: { amount: 1000, asOfDate },
    });
    const card = await createAccountViaApi(request, {
      name: `Visa ${uid}`,
      kind: 'CreditCard',
      reportingCurrencyId: cad,
      initialBalance: { amount: 500, asOfDate },
    });

    await accountListPage.goto();
    await expect(accountListPage.entryFor(cash.name)).toContainText(KIND_LABELS.Cash);
    await expect(accountListPage.entryFor(card.name)).toContainText(KIND_LABELS.CreditCard);
  });
});
