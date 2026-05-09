import { test, expect } from '../../../fixtures/finances.fixture';
import { TEST_DATA } from '../../../e2e.config';
import {
  createAccountViaApi,
  deleteAllFinancesData,
  findCurrencyId,
  setDisplayCurrencyViaApi,
  type AccountHandle,
} from '../../../helpers/api/finances';
import { isoDateDaysAgo, todayIso } from '../../../helpers/date.helpers';

const TRANSFER_MEMO = 'E2E transfer';

test.describe.configure({ mode: 'serial' });

test.describe('Finances — transfers', () => {
  let chequing: AccountHandle;
  let savings: AccountHandle;

  test.beforeAll(async ({ request }) => {
    await deleteAllFinancesData(request);
    const cadId = await findCurrencyId(request, TEST_DATA.finances.currency);
    await setDisplayCurrencyViaApi(request, cadId);
    const asOfDate = isoDateDaysAgo(30);
    chequing = await createAccountViaApi(request, {
      name: TEST_DATA.finances.accounts.chequing,
      kind: 'Cash',
      reportingCurrencyId: cadId,
      initialBalance: { amount: TEST_DATA.finances.amounts.chequingOpening, asOfDate },
    });
    savings = await createAccountViaApi(request, {
      name: TEST_DATA.finances.accounts.savings,
      kind: 'Cash',
      reportingCurrencyId: cadId,
      initialBalance: { amount: TEST_DATA.finances.amounts.savingsOpening, asOfDate },
    });
  });

  test.afterAll(async ({ request }) => {
    await deleteAllFinancesData(request);
  });

  test('transfers between two CAD cash accounts and updates both balances', async ({
    accountDetailPage,
  }) => {
    await accountDetailPage.gotoById(chequing.id);
    await expect(accountDetailPage.balanceText()).toContainText('3,000.00');

    await accountDetailPage.startTransfer({
      destinationName: TEST_DATA.finances.accounts.savings,
      amount: TEST_DATA.finances.amounts.transferAmount.toFixed(2),
      date: todayIso(),
      memo: TRANSFER_MEMO,
    });

    await expect(accountDetailPage.balanceText()).toContainText('2,500.00');

    await accountDetailPage.gotoById(savings.id);
    await expect(accountDetailPage.balanceText()).toContainText('1,500.00');
  });

  test('the transfer shows up in both accounts\' Activity tabs', async ({ accountDetailPage }) => {
    await accountDetailPage.gotoById(chequing.id);
    // Two rows = opening balance + the transfer leg.
    await expect(accountDetailPage.activityRows()).toHaveCount(2);
    await expect(accountDetailPage.page.getByText(TRANSFER_MEMO).first()).toBeVisible();

    await accountDetailPage.gotoById(savings.id);
    await expect(accountDetailPage.activityRows()).toHaveCount(2);
    await expect(accountDetailPage.page.getByText(TRANSFER_MEMO).first()).toBeVisible();
  });
});
