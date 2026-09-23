import { test, expect } from '../../fixtures/finances.fixture';
import { DISPLAY_CURRENCY, LOCKS } from '../../e2e.config';
import { createAccountViaApi, findCurrencyId, netWorthAtEndOfViaApi } from '../../helpers/api/finances';
import { escapeRegExp } from '../../helpers/playwright.helpers';

/** Formats like the Reports headline: display-currency symbol, up to two decimals. */
function formatHeadline(value: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: DISPLAY_CURRENCY,
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  }).format(value);
}

// Net worth is a household-wide sum, so it can't be isolated by name. The test instead works two
// years back — a window every other spec (which records "today" or "30 days ago") leaves alone —
// and holds a lock so its own runs on the other device projects don't write there concurrently.
test.describe('Finances — reports', { lock: LOCKS.financesNetWorth }, () => {
  test('net worth for a past year includes the accounts open then, net of liabilities', async ({
    request,
    reportsPage,
    uid,
  }) => {
    const year = new Date().getFullYear() - 2;
    const from = `${year}-01-01`;
    const to = `${year}-12-31`;
    const before = await netWorthAtEndOfViaApi(request, from, to);

    const cad = await findCurrencyId(request, DISPLAY_CURRENCY);
    await createAccountViaApi(request, {
      name: `Savings ${uid}`,
      kind: 'Cash',
      reportingCurrencyId: cad,
      initialBalance: { amount: 12000, asOfDate: `${year}-03-15` },
    });
    await createAccountViaApi(request, {
      name: `Car Loan ${uid}`,
      kind: 'Loan',
      reportingCurrencyId: cad,
      initialBalance: { amount: 4500.25, asOfDate: `${year}-06-01` },
    });

    await reportsPage.goto();
    await reportsPage.selectPreset('Last Year');
    await reportsPage.stepBack();

    await expect(reportsPage.rangeTrigger).toHaveText(`Jan ${year} – Dec ${year}`);
    // The asset adds to net worth and the loan subtracts from it.
    const expected = formatHeadline(before + 12000 - 4500.25);
    await expect(reportsPage.headline()).toHaveText(new RegExp(`^Net worth\\s*${escapeRegExp(expected)}\\s*Most recent value`));
  });
});
