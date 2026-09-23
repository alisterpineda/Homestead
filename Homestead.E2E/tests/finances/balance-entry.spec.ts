import { test, expect } from '../../fixtures/finances.fixture';
import { DISPLAY_CURRENCY } from '../../e2e.config';
import { createAccountViaApi, findCurrencyId } from '../../helpers/api/finances';
import { isoDateDaysAgo, todayIso } from '../../helpers/date.helpers';
import { formatBalance } from '../../pages/finances/account-detail.page';

test.describe('Finances — balance entries', () => {
  test('recording a balance updates the displayed balance and adds an Activity row', async ({
    request,
    accountDetailPage,
    uid,
  }) => {
    const cad = await findCurrencyId(request, DISPLAY_CURRENCY);
    const savings = await createAccountViaApi(request, {
      name: `Savings ${uid}`,
      kind: 'Cash',
      reportingCurrencyId: cad,
      initialBalance: { amount: 2000, asOfDate: isoDateDaysAgo(30) },
    });
    const memo = `Balance check ${uid}`;

    await accountDetailPage.gotoById(savings.id);
    await expect(accountDetailPage.activityRows()).toHaveCount(1);
    await expect(accountDetailPage.balanceText()).toHaveText(formatBalance(2000));

    await accountDetailPage.recordBalance({ amount: '2500.00', date: todayIso(), memo });

    // Two rows = opening balance + the new balance entry.
    await expect(accountDetailPage.balanceText()).toHaveText(formatBalance(2500));
    await expect(accountDetailPage.activityRows()).toHaveCount(2);
    await expect(accountDetailPage.activityRows().filter({ hasText: memo })).toHaveCount(1);
  });
});
