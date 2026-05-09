import { test, expect } from '../../../fixtures/finances.fixture';
import { TEST_DATA } from '../../../e2e.config';
import {
  deleteAllFinancesData,
  findCurrencyId,
  listAccountsViaApi,
  setDisplayCurrencyViaApi,
} from '../../../helpers/api/finances';
import { todayIso } from '../../../helpers/date.helpers';
import { KIND_LABELS } from '../../../pages/finances/account-list.page';

test.describe.configure({ mode: 'serial' });

test.describe('Finances — account creation', () => {
  test.beforeAll(async ({ request }) => {
    await deleteAllFinancesData(request);
    const cadId = await findCurrencyId(request, TEST_DATA.finances.currency);
    await setDisplayCurrencyViaApi(request, cadId);
  });

  test.afterAll(async ({ request }) => {
    await deleteAllFinancesData(request);
  });

  test('shows empty state when no accounts exist', async ({ accountListPage }) => {
    await accountListPage.goto();
    await expect(accountListPage.noAccountsEmpty).toBeVisible();
    await expect(accountListPage.newAccountButton.first()).toBeVisible();
  });

  test('creates a Cash account with an initial balance', async ({ accountListPage, accountDetailPage, page }) => {
    await accountListPage.goto();
    await accountListPage.openCreateDialog();
    await accountListPage.submitCreate({
      kind: 'Cash',
      name: TEST_DATA.finances.accounts.chequing,
      currencyCode: TEST_DATA.finances.currency,
      initialAmount: TEST_DATA.finances.amounts.cashOpening.toFixed(2),
      asOfDate: todayIso(),
    });

    await expect(accountDetailPage.nameHeading()).toHaveText(TEST_DATA.finances.accounts.chequing);
    await expect(accountDetailPage.balanceText()).toContainText('1,000.00');
    await expect(accountDetailPage.balanceText()).not.toContainText('-1,000');
    expect(page.url()).toMatch(/\/finances\/accounts\/[0-9a-f-]+/);
  });

  test('creates a Credit Card with an initial balance shown as positive amount owed', async ({
    accountListPage,
    accountDetailPage,
  }) => {
    await accountListPage.goto();
    await accountListPage.openCreateDialog();
    await accountListPage.submitCreate({
      kind: 'CreditCard',
      name: TEST_DATA.finances.accounts.visa,
      currencyCode: TEST_DATA.finances.currency,
      initialAmount: TEST_DATA.finances.amounts.creditCardOpening.toFixed(2),
      asOfDate: todayIso(),
    });

    await expect(accountDetailPage.nameHeading()).toHaveText(TEST_DATA.finances.accounts.visa);
    // Sign-flip: liability stored as -500 in Beancount, displayed as positive 500 owed.
    await expect(accountDetailPage.balanceText()).toContainText('500.00');
    await expect(accountDetailPage.balanceText()).not.toContainText('-500');
  });

  test('list shows both accounts with their kind labels', async ({ accountListPage, request }) => {
    await accountListPage.goto();
    await expect(accountListPage.rowFor(TEST_DATA.finances.accounts.chequing)).toBeVisible();
    await expect(accountListPage.rowFor(TEST_DATA.finances.accounts.visa)).toBeVisible();

    await expect(accountListPage.page.getByText(KIND_LABELS.Cash, { exact: true }).first()).toBeVisible();
    await expect(accountListPage.page.getByText(KIND_LABELS.CreditCard, { exact: true }).first()).toBeVisible();

    const accounts = await listAccountsViaApi(request);
    const chequing = accounts.find((a) => a.name === TEST_DATA.finances.accounts.chequing);
    const visa = accounts.find((a) => a.name === TEST_DATA.finances.accounts.visa);
    expect(chequing?.accountType).toBe('Asset');
    expect(visa?.accountType).toBe('Liability');
  });
});
