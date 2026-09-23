import { test, expect } from '../../fixtures/finances.fixture';
import { DISPLAY_CURRENCY } from '../../e2e.config';
import { createAccountViaApi, findCurrencyId } from '../../helpers/api/finances';
import { isoDateDaysAgo, todayIso } from '../../helpers/date.helpers';
import { formatBalance } from '../../pages/finances/account-detail.page';

test.describe('Finances — transfers', () => {
  test('a transfer between two cash accounts moves the balance and shows on both Activity tabs', async ({
    request,
    accountDetailPage,
    uid,
  }) => {
    const cad = await findCurrencyId(request, DISPLAY_CURRENCY);
    const asOfDate = isoDateDaysAgo(30);
    const chequing = await createAccountViaApi(request, {
      name: `Chequing ${uid}`,
      kind: 'Cash',
      reportingCurrencyId: cad,
      initialBalance: { amount: 3000, asOfDate },
    });
    const savings = await createAccountViaApi(request, {
      name: `Savings ${uid}`,
      kind: 'Cash',
      reportingCurrencyId: cad,
      initialBalance: { amount: 1000, asOfDate },
    });
    const memo = `Transfer ${uid}`;

    await accountDetailPage.gotoById(chequing.id);
    await expect(accountDetailPage.balanceText()).toHaveText(formatBalance(3000));

    await accountDetailPage.startTransfer({ destinationName: savings.name, amount: '500.00', date: todayIso(), memo });
    await expect(accountDetailPage.balanceText()).toHaveText(formatBalance(2500));
    // Two rows = opening balance + the transfer leg.
    await expect(accountDetailPage.activityRows()).toHaveCount(2);
    await expect(accountDetailPage.activityRows().filter({ hasText: memo })).toHaveCount(1);

    await accountDetailPage.gotoById(savings.id);
    await expect(accountDetailPage.balanceText()).toHaveText(formatBalance(1500));
    await expect(accountDetailPage.activityRows()).toHaveCount(2);
    await expect(accountDetailPage.activityRows().filter({ hasText: memo })).toHaveCount(1);
  });
});
