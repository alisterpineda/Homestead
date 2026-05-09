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

test.describe.configure({ mode: 'serial' });

test.describe('Finances — balance entries', () => {
  let savings: AccountHandle;

  test.beforeAll(async ({ request }) => {
    await deleteAllFinancesData(request);
    const cadId = await findCurrencyId(request, TEST_DATA.finances.currency);
    await setDisplayCurrencyViaApi(request, cadId);
    savings = await createAccountViaApi(request, {
      name: TEST_DATA.finances.accounts.savings,
      kind: 'Cash',
      reportingCurrencyId: cadId,
      initialBalance: {
        amount: TEST_DATA.finances.amounts.balanceEntryOpening,
        asOfDate: isoDateDaysAgo(30),
      },
    });
  });

  test.afterAll(async ({ request }) => {
    await deleteAllFinancesData(request);
  });

  test('records a balance entry that updates the displayed balance and adds an Activity row', async ({
    accountDetailPage,
  }) => {
    await accountDetailPage.gotoById(savings.id);
    await expect(accountDetailPage.activityRows()).toHaveCount(1);
    await expect(accountDetailPage.balanceText()).toContainText('2,000.00');

    await accountDetailPage.recordBalance({
      amount: TEST_DATA.finances.amounts.balanceEntryAsserted.toFixed(2),
      date: todayIso(),
      memo: 'E2E balance check',
    });

    // Two rows = opening balance + the new balance entry.
    await expect(accountDetailPage.balanceText()).toContainText('2,500.00');
    await expect(accountDetailPage.activityRows()).toHaveCount(2);
    await expect(accountDetailPage.page.getByText('E2E balance check')).toBeVisible();
  });
});
